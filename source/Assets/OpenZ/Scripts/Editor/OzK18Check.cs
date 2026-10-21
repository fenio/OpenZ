using System.Collections;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit K18 (menu, options and units). Two runs, each one log line "[OpenZ K18 ...] PASS n/n" or "FAIL" (also in Report).
    ///
    /// Main menu (Play, scene MainMenu): menu "OpenZ/Checks/K18 main menu: flybys + FREE CAMERA row (Play)".
    ///   M1 the main menu's flyby rotation holds L02 and L17 and not L16; M2 the Jungle card still has its preview; M3 back from the
    ///   level select with the Jungle card focused, the menu's background leaves L16 within 1 s; M4 OPTIONS, GAME tab: FREE CAMERA is
    ///   the row right under EDGE SCROLL; M5 FREE CAMERA is off when never set; M6 / M7 the CONTROLS camera row reads the off text with
    ///   the switch off and the free camera text with it on.
    /// A level (Play, after the briefing, no menu open, a quiet moment): menu "OpenZ/Checks/K18 level: free camera, zoom, dead colours, turret (Play)".
    ///   L1 the zoom limits are 25 / 200 m; L2 the standard 70 m is inside them and unchanged; L3 / L4 a zoom asked past either end
    ///   stops on it; L5 switch off, testing overlay hidden: a turn is refused (the fixed view); L6 switch on, overlay hidden: a
    ///   quarter turn and the lowest tilt are taken, the view centre stays on the focus, the minimap uses the free frame; L7 the free
    ///   camera at the closest and the widest zoom keeps the zoom's distance (it respects the limits); L8 switch off again: the fixed
    ///   view is back within 1 s; L9 / L10 the pause menu's GAME tab holds FREE CAMERA, the short CONTROLS row follows the switch;
    ///   L11 the sound table has no "Let's nuke them all!" line; L12 every dead robot body in the scene shows the dead colours (paler,
    ///   same value) and every live robot the live ones, with at least one dead red and one dead blue body measured (numbers in the
    ///   line); L13 (only with B8, OzK18Check.B8.cs) on level 16 the blue fort's north-east tower (tile 54,9) holds the blue rocket
    ///   turret with its view on the pad; L14 thrown parts tinted > 0: after pop deaths, their thrown parts and their team-coloured
    ///   pieces took the dead colours (valid only after a fight with pop deaths); L15 the scroll speed at 25 / 70 / 200 m is the one of
    ///   the old zoom span 22..230; L16 the sounds' zoom gain at 70 m is the one of the old span.
    /// The switch FREE CAMERA, the camera and the zoom are put back when it ends.
    /// Picture helper: "OpenZ/Checks/K18 frame the blue fort at the standard zoom (Play, level 16)".
    /// </summary>
    public static partial class OzK18Check
    {
        public static string Report = "not run";
        static IEnumerator run; static int waitFrame, ok, bad; static string tag;
        static readonly StringBuilder fails = new StringBuilder(), notes = new StringBuilder();
        static bool hadPref; static int prefWas;

        [MenuItem("OpenZ/Checks/K18 main menu: flybys + FREE CAMERA row (Play)")]
        public static void RunMenu() => Start("menu", MenuProof());

        [MenuItem("OpenZ/Checks/K18 level: free camera, zoom, dead colours, turret (Play)")]
        public static void RunLevel() => Start("level", LevelProof());

        [MenuItem("OpenZ/Checks/K18 frame the blue fort at the standard zoom (Play, level 16)")]
        public static void FrameBlueFort()
        {
            var g = ZGame.I; var rts = OpenZHud.I ? OpenZHud.I.rts : null;
            if (!EditorApplication.isPlaying || g == null || g.World == null || !rts) { Debug.LogWarning("[OpenZ K18] Play a level first"); return; }
            var f = g.World.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.IsFort && o.Owner == Team.Blue);
            if (f == null) return;
            var p = ZGame.PxToWorld(f.CenterX, f.CenterY + 40);
            rts.FreeLookOff(); rts.focus = new Vector3(p.x, 0f, p.z); rts.SetHeightImmediate(70f);
            Debug.Log($"[OpenZ K18] camera over the blue fort ({f.LocX / 16},{f.LocY / 16}) at the standard zoom 70 m");
        }

        static void Start(string what, IEnumerator e)
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ K18] enter Play mode first"); return; }
            if (run != null) { Debug.LogWarning("[OpenZ K18] a run is still going"); return; }
            ok = bad = 0; fails.Clear(); notes.Clear(); tag = what; Report = "running";
            hadPref = PlayerPrefs.HasKey(OzFreeCam.PrefKey); prefWas = PlayerPrefs.GetInt(OzFreeCam.PrefKey, 0);
            run = e; waitFrame = 0; EditorApplication.update += Step;
        }

        static void Step()
        {
            if (run == null || !EditorApplication.isPlaying) { run = null; EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame) return;
            bool more;
            try { more = run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); bad++; fails.Append(" | exception ").Append(ex.Message); more = false; }
            if (!more) { run = null; EditorApplication.update -= Step; Finish(); return; }
            waitFrame = Time.frameCount + (run.Current is int n ? n : 1);
        }

        static void Check(bool pass, string what) { if (pass) ok++; else { bad++; fails.Append(" | ").Append(what); } notes.Append(pass ? " [ok] " : " [FAIL] ").Append(what).Append(';'); }

        static void PutBackPref()
        {
            if (hadPref) OzFreeCam.On = prefWas == 1;
            else { OzFreeCam.On = false; PlayerPrefs.DeleteKey(OzFreeCam.PrefKey); PlayerPrefs.Save(); }
        }

        static void Finish()
        {
            PutBackPref();
            Report = $"[OpenZ K18 {tag}] {(bad == 0 ? "PASS" : "FAIL")} {ok}/{ok + bad} checks at {Screen.width}x{Screen.height}:{notes}{(bad > 0 ? " FAILED:" + fails : "")}";
            if (bad == 0) Debug.Log(Report); else Debug.LogError(Report);
        }

        static string RowTexts()
        {
            var sb = new StringBuilder();
            foreach (var r in Resources.FindObjectsOfTypeAll<OzFreeCamRowText>())
            {
                if (!r || EditorUtility.IsPersistent(r)) continue;
                r.Refresh(); if (sb.Length > 0) sb.Append(" / "); sb.Append('"').Append(r.Text).Append('"');
            }
            return sb.ToString();
        }
        static int RowCount() => Resources.FindObjectsOfTypeAll<OzFreeCamRowText>().Count(r => r && !EditorUtility.IsPersistent(r));

        static bool RowsRead(bool on)
        {
            bool any = false, all = true;
            foreach (var r in Resources.FindObjectsOfTypeAll<OzFreeCamRowText>())
            {
                if (!r || EditorUtility.IsPersistent(r)) continue;
                r.Refresh(); any = true;
                bool full = r.Text == OzFreeCam.FullRowOn || r.Text == OzFreeCam.FullRowOff;
                string want = on ? (full ? OzFreeCam.FullRowOn : OzFreeCam.CompactRowOn) : (full ? OzFreeCam.FullRowOff : OzFreeCam.CompactRowOff);
                all &= r.Text == want && r.ShowsFreeCamera == on;
            }
            return any && all;
        }

        static bool RowUnder(string rows, string above, string label)
        {
            var list = rows.Split(new[] { " | " }, System.StringSplitOptions.None).ToList();
            int i = list.IndexOf(above), j = list.IndexOf(label);
            return i >= 0 && j == i + 1;
        }

        /// <summary>B8's line (OzK18Check.B8.cs, only with apply_k18.py --with-b8); without it the line is left out.</summary>
        static partial void TurretLine(ZGame g, ref bool done);

        /// <summary>The minimap's view box as it is drawn (OpenZMinimap.Frame, in map pixels): its corners on the ground the camera's
        /// corner rays meet (the free look's flat ground plane, as the minimap draws it) within 1 map pixel, and its turn equal to the
        /// view's heading within 0.5 deg (and clearly turned, not north up).</summary>
        static bool MinimapBox(OpenZHud hud, Camera cam, ZGame g, out string why)
        {
            var mm = hud ? hud.Minimap : null; var map = g != null && g.World != null ? g.World.Map : null;
            if (!mm || !mm.MapRect || map == null || mm.Frame == null || mm.Frame.Length != 4) { why = "NO MINIMAP drawn"; return false; }
            var r = mm.MapRect.rect; var got = new Vector2[4];
            Vector2[] corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };   // the minimap's own order
            float err = 0f;
            for (int i = 0; i < 4; i++)
            {
                got[i] = new Vector2(mm.Frame[i].x / r.width * map.WidthPix, (1f - mm.Frame[i].y / r.height) * map.HeightPix);
                var ray = cam.ViewportPointToRay(new Vector3(corners[i].x, corners[i].y, 0f));
                if (ray.direction.y > -1e-4f) { err = 999f; continue; }
                var p = ray.GetPoint(Mathf.Min(-ray.origin.y / ray.direction.y, 3000f));
                err = Mathf.Max(err, Mathf.Max(Mathf.Abs(p.x * 8f - got[i].x), Mathf.Abs(-p.z * 8f - got[i].y)));
            }
            var fwd = (got[2] + got[3]) * 0.5f - (got[0] + got[1]) * 0.5f;                                // map pixels: x east, y south
            float turn = Mathf.Atan2(fwd.x, -fwd.y) * Mathf.Rad2Deg, heading = cam.transform.eulerAngles.y;
            bool ok = err <= 1f && Mathf.Abs(Mathf.DeltaAngle(turn, heading)) <= 0.5f && Mathf.Abs(Mathf.DeltaAngle(turn, 0f)) > 45f;
            why = $"turned {turn:0.##} deg (the view {heading:0.##}), corners within {err:0.##} map px of the ground the view meets{(ok ? "" : " (needs a turned box, within 1 px and 0.5 deg)")}";
            return ok;
        }

        // ------------------------------------------------------------------ main menu
        static IEnumerator MenuProof()
        {
            var m = Object.FindFirstObjectByType<OpenZMainMenu>();
            if (!m) { Check(false, "no main menu in this scene (open MainMenu)"); yield break; }
            yield return 10;
            string rot = m.ProofMenuRotation, cards = m.ProofCardPreviews;
            Check(rot.Contains("L02") && rot.Contains("L17") && !rot.Contains("L16"), $"M1 the main menu's flyby rotation: {rot}");
            Check(cards.Contains("L16"), $"M2 cards with a preview: {cards} (the Jungle card keeps its own)");
            int jungle = m.ProofLevelIndex("L16");
            m.ProofGoSelect(jungle); yield return 30;
            string inSelect = m.ProofBackgroundLevel;
            m.ProofGoMain(); float t0 = Time.unscaledTime; string bg = m.ProofBackgroundLevel;
            while (bg == "L16" && Time.unscaledTime - t0 < 1f) { yield return 1; bg = m.ProofBackgroundLevel; }
            Check(bg != "L16" && bg.Length > 0, $"M3 level select background with the Jungle card: {inSelect}; back in the main menu after {Time.unscaledTime - t0:0.00} s: {bg}");
            m.ProofOptTab(OzSettingTips.TabGame); yield return 2;
            string rows = m.ProofOptTabRows;
            Check(RowUnder(rows, "EDGE SCROLL", OzFreeCam.Label), $"M4 OPTIONS, GAME tab rows: {rows}");
            Check((hadPref && prefWas == 1) || !OzFreeCam.On, $"M5 FREE CAMERA {(OzFreeCam.On ? "ON" : "OFF")} (saved before: {(hadPref ? prefWas.ToString() : "never set")})");
            OzFreeCam.On = false; bool off = RowsRead(false); string offT = RowTexts();
            OzFreeCam.On = true; bool on = RowsRead(true); string onT = RowTexts();
            Check(off, $"M6 CONTROLS camera row, switch off ({RowCount()} rows): {offT}");
            Check(on, $"M7 CONTROLS camera row, switch on: {onT}");
            PutBackPref(); RowsRead(OzFreeCam.On);
        }

        // ------------------------------------------------------------------ a level
        static IEnumerator LevelProof()
        {
            var hud = OpenZHud.I; var g = ZGame.I; var rts = hud ? hud.rts : null;
            if (!hud || g == null || g.World == null || !rts) { Check(false, "no level running"); yield break; }
            var cam = rts.GetComponent<Camera>();
            Vector3 focus0 = rts.focus; float h0 = rts.height; bool shown0 = OzCheatSpawner.Visible;
            if (OzCheatSpawner.Visible) OzCheatSpawner.I.Show(false);
            rts.FreeLookOff(); OzFreeCam.On = false; yield return 2;

            // zoom limits
            Check(Mathf.Approximately(rts.minHeight, RtsCamera.ZoomClosest) && Mathf.Approximately(rts.maxHeight, RtsCamera.ZoomWidest), $"L1 zoom limits {rts.minHeight:0.#} / {rts.maxHeight:0.#} m (were 22 / 230)");
            Check(70f > rts.minHeight && 70f < rts.maxHeight, $"L2 the standard view 70 m inside the limits (now {h0:0.#} m)");
            rts.SetHeightImmediate(5f); float lo = rts.height; rts.SetHeightImmediate(900f); float hi = rts.height;
            Check(Mathf.Approximately(lo, RtsCamera.ZoomClosest), $"L3 asked 5 m: {lo:0.##} m");
            Check(Mathf.Approximately(hi, RtsCamera.ZoomWidest), $"L4 asked 900 m: {hi:0.##} m");
            rts.SetHeightImmediate(70f); yield return 2;

            // the switch
            bool refused = !rts.FreeTurn(new Vector2(90f / RtsCamera.FreeLookSpeed, 0f));
            Check(refused && rts.yaw == 0f && !rts.FreeLook, $"L5 switch off, overlay hidden: turn refused {refused}, yaw {rts.yaw:0.##}");
            OzFreeCam.On = true; yield return 1;
            bool took = rts.FreeTurn(new Vector2(90f / RtsCamera.FreeLookSpeed, 0f)); yield return 2;
            float yaw = rts.yaw;
            rts.FreeTurn(new Vector2(0f, 100000f)); rts.TakeShake(); yield return 2;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float off = ray.direction.y < -1e-4f ? Vector3.Distance(ray.GetPoint(-ray.origin.y / ray.direction.y), new Vector3(rts.focus.x, 0f, rts.focus.z)) : 999f;
            bool boxOk = MinimapBox(hud, cam, g, out string boxWhy);
            Check(took && Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 0.05f && Mathf.Abs(rts.Tilt - Mathf.Max(22f, RtsCamera.FreeTiltMin)) < 1e-3f && off < 0.1f && boxOk,
                  $"L6 switch on, overlay hidden: turn taken {took}, yaw {yaw:0.##}, lowest tilt {rts.Tilt:0.##}, view centre {off:0.###} m off the focus; the minimap's drawn view box: {boxWhy}");
            float worst = 0f;
            foreach (float h in new[] { RtsCamera.ZoomClosest, RtsCamera.ZoomWidest })
            {
                rts.SetHeightImmediate(h); rts.FreeTurn(new Vector2(0f, -100000f)); rts.TakeShake(); yield return 2;     // the steepest tilt: no floor in the way
                float want = h / Mathf.Sin(rts.pitch * Mathf.Deg2Rad), got = Vector3.Distance(cam.transform.position, rts.focus);
                worst = Mathf.Max(worst, Mathf.Abs(got - want));
            }
            Check(worst < 0.1f, $"L7 free camera at 25 and 200 m keeps the zoom's distance (worst {worst:0.###} m)");
            rts.SetHeightImmediate(70f);
            OzFreeCam.On = false; float tOff = Time.unscaledTime;
            while (rts.FreeLook && Time.unscaledTime - tOff < 3f) yield return 1;
            float back = Time.unscaledTime - tOff;
            Check(!rts.FreeLook && back <= 1f && rts.yaw == 0f && rts.Tilt == rts.pitch, $"L8 switch off: fixed view back in {back:0.00} s, yaw {rts.yaw}, tilt {rts.Tilt}");

            // the pause menu
            var pm = hud.Pause;
            if (pm)
            {
                pm.OpenOptions(OzSettingTips.TabGame); yield return 3;
                string rows = pm.ProofOptTabRows;
                Check(RowUnder(rows, "EDGE SCROLL", OzFreeCam.Label), $"L9 pause menu, GAME tab rows: {rows}");
                OzFreeCam.On = true; bool rOn = RowsRead(true); string tOn = RowTexts();
                OzFreeCam.On = false; bool rOff = RowsRead(false); string tOffT = RowTexts();
                Check(rOn && rOff, $"L10 CONTROLS rows here ({RowCount()}): on {tOn}; off {tOffT}");
                pm.Close(); yield return 30;
            }
            else { Check(false, "L9 no pause menu"); Check(false, "L10 no pause menu"); }
            rts.FreeLookOff();

            // the voice line
            var ta = Resources.Load<TextAsset>("OpenZ/Audio/openz_sound_events");
            bool gone = ta != null && !ta.text.Contains("alan_rdy_lets_nuke_them_all_1") && !ta.text.Contains("Let's nuke them all");
            Check(gone, $"L11 sound table without the line \"Let's nuke them all!\": {(ta == null ? "table not found" : gone ? "gone" : "STILL THERE")}");

            // dead colours
            var mpb = new MaterialPropertyBlock(); int deadId = OzDeadTint.DeadId;                      // item 228: the dead colour is TeamLit's _OZDead per renderer (1 = dead); the team paint stays live
            int dead = 0, deadOk = 0, live = 0, liveOk = 0, deadRed = 0, deadBlue = 0; string numbers = "";
            foreach (var tp in Object.FindObjectsByType<TeamPaint>(FindObjectsSortMode.None))
            {
                if (!tp || !tp.isActiveAndEnabled || !tp.IsRobotBody) continue;
                var r = tp.GetComponentsInChildren<Renderer>(false).FirstOrDefault(x => x is SkinnedMeshRenderer && x.GetComponentInParent<TeamPaint>(true) == tp);
                if (!r) continue;
                r.GetPropertyBlock(mpb, 0); float got = mpb.GetFloat(deadId);
                var liveC = OzDeadTint.LiveRobotPaint(tp); bool isDead = tp.GetComponent<OzGlowFade>() != null;
                if (isDead && tp.DeadLevel < 1f) continue;                                                    // still fading (OzDeadTint.Seconds)
                bool same = Mathf.Abs(got - (isDead ? 1f : 0f)) < 1e-3f;
                if (isDead) { dead++; if (same) deadOk++; if (tp.Team == Team.Red) deadRed++; else if (tp.Team == Team.Blue) deadBlue++; } else { live++; if (same) liveOk++; }
                if (numbers.Length < 600 && (isDead ? (tp.Team == Team.Red ? deadRed : tp.Team == Team.Blue ? deadBlue : 2) : live) == 1)
                {
                    var sv = OzDeadTint.SatVal(isDead ? OzDeadTint.Paint(liveC, 1f) : liveC); var svl = OzDeadTint.SatVal(liveC);
                    numbers += $" {(isDead ? "dead" : "live")} {tp.Team} {r.name}: _OZDead {got:0.00}, paint as drawn S {sv.x:0.000} V {sv.y:0.000} (live S {svl.x:0.000} V {svl.y:0.000});";
                }
            }
            bool bothTeams = deadRed > 0 && deadBlue > 0;
            Check(bothTeams && deadOk == dead && liveOk == live,
                  $"L12 robot paint: dead bodies {deadOk}/{dead} with the dead colours (red {deadRed}, blue {deadBlue}), live robots {liveOk}/{live} with the live ones" +
                  $"{(bothTeams ? "" : " (NEEDS at least one dead red and one dead blue body: run it again after a fight where both sides lost robots)")};{numbers}");

            // the tower turret (level 16): B8, held until the owner answers; its line comes with B8 (OzK18Check.B8.cs, apply_k18.py --with-b8)
            bool turretLine = false;
            TurretLine(g, ref turretLine);
            if (!turretLine) notes.Append(g.level == "L16" ? " (L13 the turret: B8 not applied, held);" : " (L13 the turret: level 16 only);");

            // thrown parts and pieces of pop deaths (valid only after a fight with pop deaths)
            int pops = RobotPopDeath.Pops, parts = OzDeadTint.PartsTinted, pieces = OzDeadTint.PopColoursTinted, faded = OzDeadTint.PartsFaded;
            Vector2 pl = OzDeadTint.SatValSrgb(OzDeadTint.LastPieceLive), pd = OzDeadTint.SatValSrgb(OzDeadTint.LastPieceDead);
            Color plc = OzDeadTint.LastPieceLive.linear, pdc = OzDeadTint.LastPieceDead.linear;
            float yl = 0.2126f * plc.r + 0.7152f * plc.g + 0.0722f * plc.b, yd = 0.2126f * pdc.r + 0.7152f * pdc.g + 0.0722f * pdc.b;
            bool pieceTurn = pieces > 0 && pd.x < pl.x * 0.6f && yd >= yl * 0.98f;                                 // item 228: greyer (saturation under 60 % of the live one), not darker (luminance kept)
            Check(pops > 0 && parts > 0 && pieceTurn,
                  $"L14 thrown parts given the fade > 0: {parts} thrown parts / chunks / pieces fade to the dead colour ({faded} fully faded so far), {pieces} pops whose team pieces (shoulder domes, plates) fade, pop deaths so far {pops}" +
                  (pieces > 0 ? $"; last team piece as drawn S {pl.x:0.000} -> {pd.x:0.000}, V {pl.y:0.000} -> {pd.y:0.000}, luminance {yl:0.000} -> {yd:0.000}" : "") +
                  (pops == 0 ? " (NO POP DEATH YET: valid only after a fight with pop deaths, run it again then)" : ""));

            // the scroll speed and the sounds' zoom gain keep the old zoom span (22..230 m) at every height
            float worstSpeed = 0f; string speeds = "";
            foreach (float h in new[] { RtsCamera.ZoomClosest, 70f, RtsCamera.ZoomWidest })
            {
                float was = rts.panSpeed * Mathf.Lerp(0.6f, 2.6f, Mathf.InverseLerp(22f, 230f, h)), now = rts.PanSpeedAt(h);
                worstSpeed = Mathf.Max(worstSpeed, Mathf.Abs(now - was)); speeds += $" {h:0} m {now:0.00} m/s (before {was:0.00});";
            }
            Check(worstSpeed < 1e-4f, $"L15 scroll speed as before the new limits:{speeds}");
            var au = Object.FindFirstObjectByType<CombatAudio>();
            if (au)
            {
                rts.SetHeightImmediate(70f); yield return 2;
                float gotDb = 20f * Mathf.Log10(Mathf.Max(1e-6f, au.ZoomGain)), wasDb = au.overviewDb * Mathf.InverseLerp(22f, 230f, rts.height);
                Check(Mathf.Abs(gotDb - wasDb) < 0.01f, $"L16 sounds' zoom gain at {rts.height:0.#} m: {gotDb:0.00} dB (before the new limits {wasDb:0.00} dB)");
            }
            else Check(false, "L16 no CombatAudio in the level");

            rts.FreeLookOff(); rts.focus = focus0; rts.SetHeightImmediate(h0);
            if (shown0 && OzCheatSpawner.I) OzCheatSpawner.I.Show(true);
        }
    }
}
