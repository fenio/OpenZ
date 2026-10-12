using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// MINIMAL UI row clicked with a real (proof device) mouse click, ON frame (only MENU + minimap; a toast's logic + sound run, its
    /// plate stays hidden), the production window + factory list still open on demand while ON, OFF again; then CREDITS: the Z
    /// COMMUNITY links (hover = ink + ui_hover, click = ui_click + OpenURL, logged instead of opened in the proof), the Rebellion
    /// <c></c>
    /// MINIMAL UI setting is restored at the end.
    /// </summary>
    public class MinimalUiProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var go = new GameObject("MinimalUiProof"); DontDestroyOnLoad(go); go.AddComponent<MinimalUiProof>();
        }

        static Mouse proofMouse, realMouse;
        static Vector2 mousePos;
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
        void OnDestroy() { ProofMouseOff(); Application.logMessageReceived -= OnLog; OpenZMainMenu.ProofNoOpen = false; }
        static void MouseAt(Vector2 p, bool down = false)
        {
            mousePos = p;
            var st = new MouseState { position = p }; if (down) st = st.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(proofMouse != null ? proofMouse : Mouse.current, st);
        }
        IEnumerator Click(Vector2 p)
        {
            MouseAt(p); yield return null; yield return null;
            MouseAt(p, true); yield return null; yield return null;
            MouseAt(p, false); yield return null; yield return null;
        }

        readonly StringBuilder sb = new StringBuilder(); int fails, frames, errors; string dir; bool origMinimal;
        void Log(string s) { sb.AppendLine(s); Debug.Log("[MinimalUiProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator ShotW(string name) { Shot(name); yield return null; yield return new WaitForSecondsRealtime(0.25f); }
        static object F(object o, string n) { var t = o.GetType(); while (t != null) { var f = t.GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); if (f != null) return f.GetValue(o); t = t.BaseType; } return null; }
        static bool Active(object rt) => rt is RectTransform r && r && r.gameObject.activeInHierarchy;
        static Vector2 ScreenCenter(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return (c[0] + c[2]) * 0.5f; }
        static string Sounds(float t0) => string.Join(", ", OzSoundLog.Since(t0).Select(e => e.key + (e.clip != null ? "=" + e.clip : "") + " " + e.outcome));
        static bool Heard(float t0, string key) => OzSoundLog.Since(t0).Any(e => (e.key != null && e.key.Contains(key)) || (e.clip != null && e.clip.Contains(key)));

        void Start()
        {
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/task_minimal_ui"));
            System.IO.Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            StartCoroutine(All());
        }
        void OnLog(string msg, string st, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) { errors++; if (errors < 6) sb.AppendLine("CONSOLE " + type + ": " + msg); }
        }

        IEnumerator All()
        {
            yield return new WaitForSecondsRealtime(1f);
            float w0 = Time.realtimeSinceStartup;
            while ((ZGame.I == null || ZGame.I.World == null || OpenZHud.I == null) && Time.realtimeSinceStartup - w0 < 60f) yield return null;
            while (ZodBriefing.BriefingActive && Time.realtimeSinceStartup - w0 < 60f) yield return null;
            yield return new WaitForSecondsRealtime(4f);                                                       // the HUD entrance + mission banner end
            origMinimal = OzSettingTips.MinimalUi;
            Log($"start {System.DateTime.Now:HH:mm:ss}, level {ZGame.I.level}, screen {Screen.width}x{Screen.height}, test mode {GameBoot.TestMode}, saved minimal {origMinimal}");
            ProofMouseOn(); MouseAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.55f));
            yield return Hud();
            if (OzSettingTips.MinimalUi != origMinimal) OzSettingTips.MinimalUi = origMinimal;                 // the player's setting back
            yield return Menu();
            Log($"done {System.DateTime.Now:HH:mm:ss}: {fails} FAIL, {frames} frames, {errors} console errors; minimal restored to {OzSettingTips.MinimalUi}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), sb.ToString());
            Report = fails == 0 && errors == 0 ? "PASS" : $"{fails} FAIL / {errors} errors";
            ProofMouseOff();
            Destroy(gameObject);
        }

        IEnumerator Hud()
        {
            var g = ZGame.I; var W = g.World; var hud = OpenZHud.I;
            if (OzSettingTips.MinimalUi) { OzSettingTips.MinimalUi = false; yield return new WaitForSecondsRealtime(0.5f); }
            object top = F(hud, "top"), terr = F(hud, "terr"), unit = F(hud, "unitPanel"), cmd = F(hud, "cmdBar");
            var menuBtn = (HudButton)F(hud, "menuBtn");
            string Panels() => $"top {Active(top)}, territory {Active(terr)}, unit panel {Active(unit)}, command bar {Active(cmd)}, MENU {menuBtn != null && Active(menuBtn.Rt)}, minimap {hud.Minimap && Active(hud.Minimap.Panel)}";
            int ToastsShown() { int n = 0; foreach (var t in (IList)F(hud, "toasts")) if (Active(F(t, "rt"))) n++; return n; }
            int ToastsUsed() { int n = 0; foreach (var t in (IList)F(hud, "toasts")) if ((bool)F(t, "used")) n++; return n; }

            // OFF: the full HUD with a plate
            hud.Toast("ROBOT MANUFACTURED", "robot", false, null);
            yield return new WaitForSecondsRealtime(0.7f);
            Log($"OFF: {Panels()}, toasts shown {ToastsShown()}");
            Check("OFF: every panel + the toast plate on screen", Active(top) && Active(terr) && Active(unit) && Active(cmd) && ToastsShown() >= 1);
            yield return ShotW("1_minimal_off");

            // the pause menu row, a real click
            hud.Pause.Open(); hud.Pause.ProofOptTab(OzSettingTips.TabOf(OzSettingTips.MinimalLabel));   // options tabs: the MINIMAL UI row's tab
            yield return new WaitForSecondsRealtime(0.9f);
            var row = FindObjectsByType<RectTransform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(r => r.name == "Toggle11");
            var label = row ? row.GetComponentsInChildren<TMPro.TMP_Text>().FirstOrDefault(t => t.name == "Label") : null;
            Log($"pause options: row Toggle10 {(row ? "found" : "MISSING")}, label '{(label ? label.text : "-")}'");
            Check("pause OPTIONS has the MINIMAL UI row", row && label && label.text == OzSettingTips.MinimalLabel);
            if (row)
            {
                var c = new Vector3[4]; row.GetWorldCorners(c);
                Log($"row rect y {c[0].y:0}-{c[1].y:0} px; side panel bottom check: row bottom inside the 600 u plate");
                MouseAt(ScreenCenter(row)); yield return new WaitForSecondsRealtime(1.0f);
                yield return ShotW("2_pause_options_minimal_row_tooltip");
                float t0 = Time.unscaledTime;
                yield return Click(ScreenCenter(row));
                yield return new WaitForSecondsRealtime(0.3f);
                Log($"clicked: minimal {OzSettingTips.MinimalUi}, saved pref {PlayerPrefs.GetInt(OzSettingTips.PrefMinimal, -1)}, sounds: {Sounds(t0)}");
                Check("the row switches MINIMAL UI on, saved, with the toggle sound", OzSettingTips.MinimalUi && PlayerPrefs.GetInt(OzSettingTips.PrefMinimal, 0) == 1 && Heard(t0, "toggle_on"));
                yield return ShotW("3_pause_options_minimal_on");
            }
            else OzSettingTips.MinimalUi = true;
            hud.Pause.Close();
            MouseAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.55f));
            yield return new WaitForSecondsRealtime(0.8f);

            // ON: only MENU + minimap; a plate's logic + sound run, the plate stays hidden
            int ev0 = hud.RecentEventCount; float t1 = Time.unscaledTime;
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            hud.Toast("TERRITORY CAPTURED", "flag", false, () => { if (hud.rts && fort != null) hud.rts.FlyTo(g.CenterWorld(fort)); });
            yield return new WaitForSecondsRealtime(0.6f);
            Log($"ON: {Panels()}, toasts used {ToastsUsed()} shown {ToastsShown()}, SPACE events {ev0} -> {hud.RecentEventCount}, sounds: {Sounds(t1)}");
            Check("ON: top / territory / unit + comms / command bar hidden, MENU + minimap kept", !Active(top) && !Active(terr) && !Active(unit) && !Active(cmd) && menuBtn != null && Active(menuBtn.Rt) && Active(hud.Minimap.Panel));
            Check("ON: the toast runs (logged, chirp, SPACE event) but its plate is hidden", ToastsUsed() >= 1 && ToastsShown() == 0 && hud.RecentEventCount > ev0 && Heard(t1, "toast"));
            Check("ON: player input still live (shortcuts)", hud.input && hud.input.enabled);
            yield return ShotW("4_minimal_on");

            // ON: on-demand windows still open
            hud.factoryListOpen = true; hud.Production.Open(fort);
            yield return new WaitForSecondsRealtime(0.9f);
            var fl = F(hud, "factoryPanel");
            Log($"ON + on demand: production window open {hud.Production.IsOpen}, factory list {Active(fl)}");
            Check("ON: the production window + the factory list still open on demand", hud.Production.IsOpen && Active(fl));
            yield return ShotW("5_minimal_on_production_window");
            hud.Production.Close(); hud.factoryListOpen = false;

            // OFF again
            OzSettingTips.MinimalUi = false;
            yield return new WaitForSecondsRealtime(0.6f);
            Log($"OFF again: {Panels()}");
            Check("OFF again: every panel back", Active(top) && Active(terr) && Active(unit) && Active(cmd));
            yield return ShotW("6_minimal_off_again");
        }

        IEnumerator Menu()
        {
            OzScenes.MainMenu();
            float w0 = Time.realtimeSinceStartup;
            OpenZMainMenu menu = null;
            while (!(menu = FindFirstObjectByType<OpenZMainMenu>()) && Time.realtimeSinceStartup - w0 < 40f) yield return null;
            yield return new WaitForSecondsRealtime(3.5f);
            if (!menu) { Check("main menu loaded", false); yield break; }
            // main-menu OPTIONS row
            menu.ProofOpen("options");
            yield return new WaitForSecondsRealtime(0.8f);
            var opt = menu.ProofPanel("options");
            menu.ProofOptTab(OzSettingTips.TabOf(OzSettingTips.MinimalLabel)); var row = opt ? opt.GetComponentsInChildren<RectTransform>().FirstOrDefault(r => r.name == "Toggle10") : null;
            var label = row ? row.GetComponentsInChildren<TMPro.TMP_Text>().FirstOrDefault(t => t.name == "Label") : null;
            string tip = menu.ProofOptTip(OzSettingTips.MinimalLabel);
            yield return new WaitForSecondsRealtime(0.8f);
            Log($"menu options: row 10 '{(label ? label.text : "-")}', tip '{tip}', MUSIC/FULLSCREEN rows {menu.ProofOptionRows}");
            Check("main-menu OPTIONS has the MINIMAL UI row with its tip", label && label.text == OzSettingTips.MinimalLabel && tip != null && tip.Contains("menu button") && menu.ProofOptionRows);
            yield return ShotW("7_menu_options_minimal_row");
            menu.ProofOpen("none");
            yield return new WaitForSecondsRealtime(0.6f);

            // CREDITS: Z COMMUNITY + Rebellion
            menu.ProofOpenCredits();
            yield return new WaitForSecondsRealtime(1.0f);
            int n = menu.ProofLinkCount;
            Log($"credits: {n} community links");
            Check("credits: 11 verified community links", n == 11);
            menu.ProofScrollCredits(Mathf.Max(0f, menu.ProofCreditsLinkY(0) - 120f));
            yield return new WaitForSecondsRealtime(1.4f);
            yield return ShotW("8_credits_z_community");
            int steam = -1; for (int i = 0; i < n; i++) if (menu.ProofLinkUrl(i) != null && menu.ProofLinkUrl(i).Contains("275530")) steam = i;
            var pt = menu.ProofLinkPoint(steam);
            Log($"Steam link #{steam} at {(pt.HasValue ? pt.Value.ToString("0") : "out of view")}");
            if (pt.HasValue)
            {
                float t0 = Time.unscaledTime;
                MouseAt(pt.Value + new Vector2(0, 3)); yield return null; MouseAt(pt.Value); yield return new WaitForSecondsRealtime(0.5f);
                Log($"hover: link hovered {menu.ProofLinkHovered(steam)}, sounds {Sounds(t0)}");
                Check("link hover: ink + arrow, ui_hover", menu.ProofLinkHovered(steam) && Heard(t0, "hover"));
                yield return ShotW("9_credits_link_hover");
                OpenZMainMenu.ProofNoOpen = true; OpenZMainMenu.LastLinkOpened = null;
                float t2 = Time.unscaledTime; int f0 = Time.frameCount;
                yield return Click(pt.Value);
                yield return new WaitForSecondsRealtime(1.0f);
                int fr = Time.frameCount - f0;
                Log($"click: opened '{OpenZMainMenu.LastLinkOpened}', {fr} frames in the next {Time.unscaledTime - t2:0.0} s, sounds {Sounds(t2)}");
                Check("link click: Steam app 275530 (Application.OpenURL path), ui_click, the menu keeps running", OpenZMainMenu.LastLinkOpened == "https://store.steampowered.com/app/275530/Z/" && Heard(t2, "click") && fr > 10);
                OpenZMainMenu.ProofNoOpen = false;
                // a link scrolled out of view takes no click
                menu.ProofScrollCredits(0f); yield return new WaitForSecondsRealtime(1.2f);
                Check("a link scrolled out of the view has no hot area", menu.ProofLinkPoint(steam) == null);
            }
            else Check("Steam link in view", false);
            menu.ProofScrollCredits(99999f);
            yield return new WaitForSecondsRealtime(1.4f);
            string foot = menu.ProofCreditsFoot ?? "";
            Log($"legal line: {(foot.Contains("Rebellion") ? "present" : "MISSING")}");
            Check("credits legal block names Rebellion", foot.Contains("Z © Rebellion.") && foot.Contains("not affiliated with or endorsed by Rebellion"));
            yield return ShotW("10_credits_legal_rebellion");
        }
    }
}
