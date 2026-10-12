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
    /// (7) fort frame = its silhouette, (5) the production window holds its screen spot while the camera scrolls, (12) the veteran chip
    /// fits its text, (24) a destroyed factory's TECH tag goes, (M1 + M2) a pause-menu option tooltip drawn over the menu; then the main
    /// menu: (M2) an OPTIONS row tooltip, (M3) the clicked map card stays selected while the mouse hovers the other card and leaves;
    /// then L17 in the full look: (25) Rachel's car-park tips spaced out. Real Input System mouse events on a proof mouse device (the
    /// </summary>
    public class BundleExe2UiProof : MonoBehaviour
    {
        public static string Report = "idle";
        static bool l17;
        public static void Run(bool withL17 = true)
        {
            Report = "running"; l17 = withL17;
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var go = new GameObject("BundleExe2UiProof"); DontDestroyOnLoad(go); go.AddComponent<BundleExe2UiProof>();
        }

        static Mouse proofMouse, realMouse;
        void ProofMouseOn()
        {
            realMouse = Mouse.current;
            proofMouse = InputSystem.AddDevice<Mouse>("OzProofMouse");
            if (realMouse != null) InputSystem.DisableDevice(realMouse);
            proofMouse.MakeCurrent();
        }
        static void ProofMouseOff()
        {
            if (realMouse != null) { InputSystem.EnableDevice(realMouse); realMouse.MakeCurrent(); realMouse = null; }
            if (proofMouse != null) InputSystem.RemoveDevice(proofMouse);
            proofMouse = null;
        }
        void OnDestroy() { ProofMouseOff(); Application.logMessageReceived -= OnLog; }
        static void MouseAt(Vector2 p) { InputSystem.QueueStateEvent(proofMouse != null ? proofMouse : Mouse.current, new MouseState { position = p }); }

        readonly StringBuilder sb = new StringBuilder(); int fails, frames; string dir;
        void Log(string s) { sb.AppendLine(s); Debug.Log("[Exe2UiProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        /// <summary>The capture lands at the end of this frame: hold one more frame + a beat so the next step is not in the picture.</summary>
        IEnumerator ShotW(string name) { Shot(name); yield return null; yield return new WaitForSecondsRealtime(0.25f); }
        static object F(object o, string n) { var t = o.GetType(); while (t != null) { var f = t.GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); if (f != null) return f.GetValue(o); t = t.BaseType; } return null; }
        static Transform Deep(Transform r, string n) { if (!r) return null; if (r.name == n) return r; foreach (Transform c in r) { var x = Deep(c, n); if (x) return x; } return null; }
        static Vector2 ScreenCenter(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return (c[0] + c[2]) * 0.5f; }
        static Rect ProjBox(Camera cam, Bounds b)
        {
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++) { var s = cam.WorldToScreenPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))); mn = Vector2.Min(mn, s); mx = Vector2.Max(mx, s); }
            return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
        }

        void Start()
        {
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/bundle_exe2_ui"));
            System.IO.Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            StartCoroutine(All());
        }

        readonly System.Collections.Generic.List<(float t, string line)> tipLog = new System.Collections.Generic.List<(float, string)>();
        void OnLog(string msg, string st, LogType type)
        {
            if (msg.StartsWith("[VoiceV3] L17 tip")) tipLog.Add((Time.time, msg));
            if (type == LogType.Exception || type == LogType.Error) { errors++; if (errors < 6) sb.AppendLine("CONSOLE " + type + ": " + msg); }
        }
        int errors;

        IEnumerator All()
        {
            yield return new WaitForSecondsRealtime(1f);
            float w0 = Time.realtimeSinceStartup;
            while ((ZGame.I == null || ZGame.I.World == null || OpenZHud.I == null) && Time.realtimeSinceStartup - w0 < 60f) yield return null;
            while (ZodBriefing.BriefingActive && Time.realtimeSinceStartup - w0 < 60f) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);
            Log($"start {System.DateTime.Now:HH:mm:ss}, level {ZGame.I.level}, screen {Screen.width}x{Screen.height}, test mode {GameBoot.TestMode}");
            ProofMouseOn(); MouseAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.55f));
            yield return L02();
            yield return Menu();
            if (l17) yield return L17();
            Log($"done {System.DateTime.Now:HH:mm:ss}: {fails} FAIL, {frames} frames, {errors} console errors");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), sb.ToString());
            Report = fails == 0 && errors == 0 ? "PASS" : $"{fails} FAIL / {errors} errors";
            ProofMouseOff();
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------------------------------------------------ L02
        IEnumerator L02()
        {
            var g = ZGame.I; var W = g.World; var hud = OpenZHud.I; var input = hud.input; var rts = hud.rts; var cam = rts.GetComponent<Camera>();
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            int ftx = (int)fort.PosX / 16, fty = (int)fort.PosY / 16;
            // (6a) jeep frame
            var jeep = W.Objects.FirstOrDefault(o => o.Type == "jeep" && !o.Destroyed && o.Owner == g.player);
            if (jeep == null)
            {
                int tx = ftx - 6, ty = fty - 4; OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
                jeep = W.CreateObject("vehicle", "jeep", tx * 16, ty * 16, g.player); g.SpawnUnitView(jeep);
            }
            rts.FlyTo(g.CenterWorld(jeep)); rts.ZoomTo(34f);
            yield return new WaitForSecondsRealtime(2.2f);
            input.Selection.Clear(); input.Selection.Add(jeep);
            yield return new WaitForSecondsRealtime(0.6f);
            var jv = g.Views[jeep.Id];
            input.MeshRect(jeep, out var jr);
            var gun = jv.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith("Gun_")).ToList();
            var chassis = jv.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Chassis");
            float gunTop = gun.Count > 0 ? gun.Max(r => ProjBox(cam, r.bounds).yMax) : -1f;
            Log($"(6a) jeep {jeep.Id}: frame {jr.width:0}x{jr.height:0} px top {jr.yMax:0}; gun parts {gun.Count} top {gunTop:0}; chassis box top {(chassis ? ProjBox(cam, chassis.bounds).yMax : -1):0}");
            Check("(6a) jeep frame from the body: the MG on top is outside the frame", gun.Count > 0 && gunTop > jr.yMax + 2f);
            yield return ShotW("6a_jeep_frame_body_only");
            input.Selection.Clear();
            // (7) fort frame
            rts.FlyTo(g.CenterWorld(fort)); rts.ZoomTo(60f);
            yield return new WaitForSecondsRealtime(3.5f);                                               // the zoom eases out: open on a still camera
            hud.Production.Open(fort);
            yield return new WaitForSecondsRealtime(0.8f);
            input.MeshRect(fort, out var fr);
            var fbase = g.StaticViews[fort.Id].GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "FortBase");
            var box = fbase ? ProjBox(cam, fbase.bounds) : default;
            Log($"(7) fort {fort.Id}: silhouette {input.ProofSilhouettePoints(fort.Id)} points, frame {fr.width:0}x{fr.height:0} px top {fr.yMax:0}; old box top {box.yMax:0} ({box.yMax - fr.yMax:0} px higher), box {box.width:0}x{box.height:0}");
            Check("(7) fort frame fits the fort (silhouette, top well under the old box top)", input.ProofSilhouettePoints(fort.Id) > 50 && box.yMax - fr.yMax > 10f);
            yield return ShotW("7_fort_frame_and_window");
            // (5) the production window holds its screen spot while the camera scrolls
            var p0 = hud.Production.ProofWindowPos; int pins0 = OpenZProductionWindow.PinCount;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.4f) { rts.focus += new Vector3(9f, 0f, 6f) * Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.2f);
            var p1 = hud.Production.ProofWindowPos;
            Log($"(5) window {p0} -> {p1} after a 1.4 s scroll of {new Vector3(9f, 0f, 6f) * 1.4f} m; re-pins {OpenZProductionWindow.PinCount - pins0}; still open {hud.Production.IsOpen}");
            Check("(5) production window does not move while scrolling", hud.Production.IsOpen && (p1 - p0).magnitude < 0.5f && OpenZProductionWindow.PinCount == pins0);
            yield return ShotW("5_window_after_scroll");
            hud.Production.Close();
            // (12) veteran chip
            var grunt = W.Objects.Where(o => o.Kind == ZKind.Robot && o.Owner == g.player && !o.Destroyed && o.Leader == null).OrderBy(o => (o.CenterX - fort.CenterX) * (o.CenterX - fort.CenterX) + (o.CenterY - fort.CenterY) * (o.CenterY - fort.CenterY)).FirstOrDefault();
            if (grunt != null)
            {
                rts.FlyTo(g.CenterWorld(grunt)); rts.ZoomTo(30f);
                yield return new WaitForSecondsRealtime(2.2f);
                var vm = FindFirstObjectByType<VeteranMarks>();
                var mi = typeof(VeteranMarks).GetMethod("OnPromoted", BindingFlags.NonPublic | BindingFlags.Instance);
                mi.Invoke(vm, new object[] { grunt });
                yield return new WaitForSecondsRealtime(0.9f);
                var pop = GameObject.Find("Promoted");
                Log($"(12) promoted {grunt}: chip {VeteranMarks.ChipWidth:0} u wide (text + 22 u padding; was a fixed 64 u box), pop shown {(pop ? pop.activeInHierarchy : false)}");
                Check("(12) veteran chip sized to its text", VeteranMarks.ChipWidth > 64f && pop != null);
                yield return ShotW("12_veteran_chip");
            }
            else Check("(12) a red robot to promote", false);
            // (24) destroyed factory: its TECH tag goes
            var fac = W.Objects.Where(o => o.IsProducer && !o.IsFort && !o.Destroyed && o.Owner != g.player).OrderBy(o => o.Owner == Team.Neutral ? 1 : 0).FirstOrDefault();
            if (fac != null)
            {
                rts.FlyTo(g.CenterWorld(fac)); rts.ZoomTo(60f);
                yield return new WaitForSecondsRealtime(2.2f);
                yield return ShotW("24_factory_before");
                int off0 = OpenZUnitOverlay.DeadTagsOff;
                fac.SetHealth(0); W.UpdateObjectHealth(fac);
                yield return new WaitForSecondsRealtime(3f);
                bool drawn = OpenZUnitOverlay.BadgeRects.ContainsKey(fac.Id);
                var ov = FindFirstObjectByType<OpenZUnitOverlay>();
                var ls = ov ? F(ov, "levelStates") as IDictionary : null; object st = ls != null && ls.Contains(fac.Id) ? ls[fac.Id] : null;
                bool want = st != null && (bool)F(st, "want");
                var tags = ov ? F(ov, "lvlTags") as IList : null; int active = 0;
                if (tags != null) foreach (var t in tags) if (((RectTransform)F(t, "Rt")).gameObject.activeSelf) active++;
                Log($"(24) {fac.Type} {fac.Id} ({fac.Owner}) destroyed: badge / level tag drawn 3 s later {drawn}, tag wanted {want}, tags switched off {OpenZUnitOverlay.DeadTagsOff - off0}, level tags on screen {active}");
                Check("(24) the destroyed factory shows no TECH tag / badge", fac.Destroyed && !want && !drawn);
                yield return ShotW("24_factory_destroyed_no_tag");
            }
            else Check("(24) a factory to destroy", false);
            // (M1 + M2) pause menu tooltip over the menu
            rts.FlyTo(g.CenterWorld(fort));
            hud.Pause.Open(); hud.Pause.ProofOptTab(OzSettingTips.TabOf(OzSettingTips.CalmLabel));   // options tabs: the CALM SCREEN row's tab
            yield return new WaitForSecondsRealtime(0.5f);
            var pc = GameObject.Find("OpenZ_Pause"); var pcv = pc ? pc.GetComponent<Canvas>() : null;
            var row = pc ? Deep(pc.transform, "Toggle3") as RectTransform : null;
            if (row) MouseAt(ScreenCenter(row));
            yield return new WaitForSecondsRealtime(0.9f);
            var tip = F(hud, "tipPanel") as RectTransform; var tc = tip ? tip.GetComponent<Canvas>() : null;
            var hb = hud.HotButton;
            var tg = F(hud.Pause, "toggles") as IList; int withTip = 0, rows = 0;
            if (tg != null) foreach (var t in tg) { rows++; var b = F(t, "row") as HudButton; if (b != null && !string.IsNullOrEmpty(b.Tooltip)) withTip++; }
            var sp = F(hud.Pause, "speedBtns") as IList; int spTip = 0; if (sp != null) foreach (HudButton b in sp) if (!string.IsNullOrEmpty(b.Tooltip)) spTip++;
            Log($"(M1) hovered '{(hb != null ? hb.Tooltip : "-")}', tooltip shown {(tip ? tip.gameObject.activeInHierarchy : false)}, tooltip canvas order {(tc ? tc.sortingOrder : -1)} vs pause menu {(pcv ? pcv.sortingOrder : -1)}");
            Log($"(M2) pause rows with a tip {withTip}/{rows}, speed buttons {spTip}/{(sp != null ? sp.Count : 0)}, label '{OzSettingTips.CalmLabel}'");
            Check("(M1) the option tooltip is drawn over the pause menu", tip && tip.gameObject.activeInHierarchy && tc && pcv && tc.sortingOrder > pcv.sortingOrder && hb != null && hb.Tooltip != null && hb.Tooltip.StartsWith("CALM SCREEN"));
            Check("(M2) every pause option row has a tooltip", rows > 0 && withTip == rows && spTip == (sp != null ? sp.Count : -1));
            yield return ShotW("M1_M2_pause_tooltip_over_menu");
            MouseAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.55f));
            hud.Pause.Close();
            yield return new WaitForSecondsRealtime(0.4f);
        }

        // ------------------------------------------------------------------------------------------------------------ menu
        IEnumerator Menu()
        {
            OzScenes.MainMenu();
            float w0 = Time.realtimeSinceStartup;
            OpenZMainMenu menu = null;
            while (!(menu = FindFirstObjectByType<OpenZMainMenu>()) && Time.realtimeSinceStartup - w0 < 40f) yield return null;
            yield return new WaitForSecondsRealtime(3.5f);
            if (!menu) { Check("main menu loaded", false); yield break; }
            // (M2) OPTIONS tooltip
            menu.ProofOpen("options");
            yield return new WaitForSecondsRealtime(0.7f);
            string tip = menu.ProofOptTip(OzSettingTips.CalmLabel);
            yield return new WaitForSecondsRealtime(0.8f);
            var trt = menu.ProofOptTipRt;
            Log($"(M2) menu: {menu.ProofOptTipCount} option controls with a tip, hovered CALM row -> '{tip}', plate shown {(trt ? trt.gameObject.activeInHierarchy : false)}");
            Check("(M2) main-menu option tooltip shown beside the OPTIONS modal", tip != null && trt && trt.gameObject.activeInHierarchy && menu.ProofOptTipCount >= 15);
            yield return ShotW("M2_menu_options_tooltip");
            menu.ProofOpen("none");
            yield return new WaitForSecondsRealtime(0.6f);
            // (M3) the clicked card stays selected
            menu.ProofSelect();
            yield return new WaitForSecondsRealtime(1.6f);
            var c02 = GameObject.Find("Card_L02")?.GetComponent<RectTransform>(); var c17 = GameObject.Find("Card_L17")?.GetComponent<RectTransform>();
            if (!c02 || !c17) { Check("(M3) cards found", false); yield break; }
            MouseAt(ScreenCenter(c17)); yield return new WaitForSecondsRealtime(0.4f);
            menu.ProofClickAt(ScreenCenter(c17)); yield return new WaitForSecondsRealtime(0.5f);
            int f1 = (int)F(menu, "focusCard");
            MouseAt(ScreenCenter(c02)); yield return new WaitForSecondsRealtime(0.9f);
            int f2 = (int)F(menu, "focusCard"); int hov = (int)F(menu, "hoverCard");
            yield return ShotW("M3_L17_clicked_hovering_L02");
            MouseAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.93f)); yield return new WaitForSecondsRealtime(0.9f);
            int f3 = (int)F(menu, "focusCard");
            yield return ShotW("M3_L17_clicked_mouse_away");
            Log($"(M3) click L17 -> selected {f1}; hover L02 -> selected {f2} (hover {hov}); mouse away -> selected {f3}");
            Check("(M3) the clicked map card stays selected (hover elsewhere, mouse away)", f1 == 1 && f2 == 1 && hov == 0 && f3 == 1);
            menu.ProofClickAt(ScreenCenter(c02)); yield return new WaitForSecondsRealtime(0.4f);
            Check("(M3) clicking the other card selects it", (int)F(menu, "focusCard") == 0);
            if (!l17) yield break;
            menu.ProofClickAt(ScreenCenter(c17)); yield return new WaitForSecondsRealtime(0.3f);
            PlayerPrefs.SetInt("oz_testmode", 0);                                                       // L17 tips are off in test mode (restored after)
            typeof(OpenZMainMenu).GetMethod("Deploy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        }

        // ------------------------------------------------------------------------------------------------------------ L17
        IEnumerator L17()
        {
            float w0 = Time.realtimeSinceStartup;
            while ((ZGame.I == null || ZGame.I.level != "L17" || ZGame.I.World == null) && Time.realtimeSinceStartup - w0 < 90f) yield return null;
            PlayerPrefs.SetInt("oz_testmode", 1);
            if (ZGame.I == null || ZGame.I.level != "L17") { Check("(25) L17 loaded", false); yield break; }
            float hand = -1f; w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 170f && tipLog.Count < 2)
            {
                if (hand < 0f && !ZodBriefing.BriefingActive) hand = Time.time;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            foreach (var t in tipLog) Log($"(25) {t.t - hand:0.0} s after hand-over: {t.line}");
            float first = tipLog.Count > 0 ? tipLog[0].t - hand : -1f, gap = tipLog.Count > 1 ? tipLog[1].t - tipLog[0].t : -1f;
            Log($"(25) first tip {first:0.0} s after the hand-over (was 9 s), gap to the next {gap:0.0} s (was 22 s, steal warning 10 s); quiet {OpenZVoiceV3.TipQuiet} s after any voice");
            Check("(25) Rachel's L17 tips spaced out (first >= 18 s, next >= 45 s later)", tipLog.Count >= 2 && first >= 17.5f && gap >= 44.5f);
            yield return ShotW("25_L17_after_tips");
        }
    }
}
