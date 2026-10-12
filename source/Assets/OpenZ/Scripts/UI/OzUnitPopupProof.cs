#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Proof walk of the build menu's unit card (editor only: this file compiles to nothing in a player build).
    /// Menu OpenZ / UI / Unit Card / Proof Walk (Play), in Play on a level, once per Game view size (1920x1080, 2560x1440,
    /// 2560x1600). For every interface scale, every own fort / robot factory / vehicle factory, every build level its list
    /// has, three places of the menu on the screen (building in the middle, near the left edge, near the right edge) and
    /// every portrait, it puts the card up for that portrait (OzUnitPopup.ProofHover) and reads the REAL rectangles:
    ///   A the card is up and shows that unit            B card and its shadow inside the screen
    ///   C card clear of the hovered portrait and the portraits next to it
    ///   D card clear of every fixed HUD panel that is on screen (where the build menu does not already cover that panel; a
    ///     panel whose own canvas is switched off, as the lower panels are while the build menu is open, is not on screen) and
    ///     clear of the whole Zod transmission window when that is up
    ///   E every text inside its box at its full size (not shrunk, not cut)
    ///   F no two texts or bars of the card touch, all of them inside the card
    ///   G every shown number, name and line equals the rule tables (own arithmetic here) and a living unit of that type
    ///   H the placement rule never fell back to its last resort   I no graphic of the card takes a click
    /// plus, once: the card goes when the menu closes and when the HUD hides, and its per-frame work allocates 0 bytes.
    /// Pass = 0 violations. A cropped picture of every case of the middle place is saved (JPG), the look-at set as PNG
    /// (scales 90 / 100 / 160 % and the screen's default, each producer at its highest level), and one full frame per menu.
    /// while it runs and each building's level, the interface scale, the camera and the time scale are put back afterwards.
    /// </summary>
    public sealed class OzUnitPopupProof : MonoBehaviour
    {
        static readonly float[] BaseScales = { 0.9f, 1f, 1.1f, 1.15f, 1.2f, 1.3f, 1.4f, 1.45f, 1.5f, 1.6f };
        static readonly string[] SkipRoot = { "ProductionWindow", "ProductionLeader", "UnitPopup", "Tooltip" };
        static readonly string[] PanelNames = { "TopStrip", "Territory", "MenuButton", "UnitPanel", "AlertTab", "CommandBar", "FactoryList", "Minimap", "MissionBanner", "FortAlert" };
        static readonly string[] PanelPrefixes = { "StoredCannon", "Toast", "Cheat" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Vector3[] corners = new Vector3[4];

        [MenuItem("OpenZ/UI/Unit Card/Proof Walk (Play)")]
        static void Run()
        {
            if (!Application.isPlaying) { Debug.LogError("[UnitCardProof] enter Play on a level first"); return; }
            if (FindFirstObjectByType<OzUnitPopupProof>()) { Debug.LogWarning("[UnitCardProof] already running"); return; }
            new GameObject("OzUnitPopupProof").AddComponent<OzUnitPopupProof>();
        }

        OpenZHud hud; string dir;
        readonly StringBuilder index = new StringBuilder();
        readonly List<string> fails = new List<string>();
        readonly int[] cat = new int[9];
        readonly HashSet<string> otherRootHits = new HashSet<string>();
        int cases, pictures;

        void Fail(int c, string where, string what)
        {
            cat[c]++;
            if (fails.Count < 400) fails.Add((char)('A' + c) + "  " + where + ": " + what);
        }

        static Rect Px(RectTransform rt)
        {
            rt.GetWorldCorners(corners);                                                   // overlay canvas: world = screen pixels
            return Rect.MinMaxRect(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y), Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
        }
        static bool Over(Rect a, Rect b, float tol = 0.5f) => a.xMin < b.xMax - tol && b.xMin < a.xMax - tol && a.yMin < b.yMax - tol && b.yMin < a.yMax - tol;
        static bool Inside(Rect a, Rect b, float tol = 0.5f) => a.xMin >= b.xMin - tol && a.yMin >= b.yMin - tol && a.xMax <= b.xMax + tol && a.yMax <= b.yMax + tol;
        static Rect Cut(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));
        static string Plain(string s) => Regex.Replace(s ?? "", "<[^>]+>", "");
        static string R(Rect r) => "(" + r.xMin.ToString("0", Inv) + "," + r.yMin.ToString("0", Inv) + ")-(" + r.xMax.ToString("0", Inv) + "," + r.yMax.ToString("0", Inv) + ")";
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Wait(float s) { float e = Time.unscaledTime + s; while (Time.unscaledTime < e) yield return null; }

        IEnumerator Start()
        {
            hud = OpenZHud.I;
            if (!hud || hud.W == null || !hud.Production) { Debug.LogError("[UnitCardProof] no HUD / world in this scene"); Destroy(gameObject); yield break; }
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            dir = Path.Combine(root, "work", "records", "milestones", "unit_card_proof", Screen.width + "x" + Screen.height);
            Directory.CreateDirectory(Path.Combine(dir, "cases")); Directory.CreateDirectory(Path.Combine(dir, "look")); Directory.CreateDirectory(Path.Combine(dir, "frames"));

            var producers = new List<ZObject>(); var seen = new HashSet<string>();
            foreach (var o in hud.W.Objects)
                if (o.IsProducer && o.Owner == hud.Player && !o.Destroyed && seen.Add(o.IsFort ? "fort" : o.Type)) producers.Add(o);
            if (producers.Count == 0) { Debug.LogError("[UnitCardProof] the player owns no fort or factory here"); Destroy(gameObject); yield break; }

            var scales = new List<float>(BaseScales);
            float def = OzText.DefaultScale; bool hasDef = false;
            foreach (var s in scales) if (Mathf.Abs(s - def) < 0.005f) hasDef = true;
            if (!hasDef) scales.Add(def);

            float scale0 = OzText.UiScale, time0 = Time.timeScale; Vector3 focus0 = hud.rts ? hud.rts.focus : Vector3.zero;
            int lastResort0 = OzUnitPopup.LastResortCount;
            Time.timeScale = 0f;                                                           // the Sim stands still while levels are forced
            index.AppendLine("# Unit card proof walk, " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ", " + Screen.width + "x" + Screen.height);
            index.AppendLine();

            foreach (float s in scales)
            {
                OzText.UiScale = s; hud.ApplyUiScale();
                yield return Frames(3);
                int before = 0; for (int i = 0; i < cat.Length; i++) before += cat[i];
                int cases0 = cases;
                bool look = Mathf.Abs(s - 0.9f) < 0.005f || Mathf.Abs(s - 1f) < 0.005f || Mathf.Abs(s - 1.6f) < 0.005f || Mathf.Abs(s - def) < 0.005f;
                foreach (var b in producers)
                {
                    int level0 = b.Level, bonus = b.BuildLevel - b.Level;
                    int top = -1; for (int lv = 5; lv >= 0 && top < 0; lv--) if (lv - bonus >= 0) top = lv;
                    for (int lv = 0; lv <= 5; lv++)
                    {
                        if (lv - bonus < 0) continue;
                        b.Level = lv - bonus;
                        if (b.BuildLevel != lv) continue;
                        for (int place = 0; place < 3; place++)
                        {
                            hud.Production.Close();
                            if (hud.rts)
                            {
                                var bp = hud.game.CenterWorld(b);
                                hud.rts.focus = bp;
                                Time.timeScale = time0; yield return Frames(4); Time.timeScale = 0f;   // the camera may need running time to get there
                                if (place > 0 && Camera.main)
                                {
                                    Vector3 mid, side;
                                    if (Ground(new Vector2(0.5f, 0.5f), bp.y, out mid) && Ground(new Vector2(place == 1 ? 0.14f : 0.86f, 0.5f), bp.y, out side))
                                    { hud.rts.focus = bp + (mid - side); Time.timeScale = time0; yield return Frames(4); Time.timeScale = 0f; }
                                }
                            }
                            hud.Production.Open(b);
                            yield return Wait(0.35f);                                         // the menu's own opening is over
                            int n = OzUnitPopup.TileCount;
                            for (int i = 0; i < n; i++)
                            {
                                HudButton btn; string unit; int col, row;
                                if (!OzUnitPopup.Tile(i, out btn, out unit, out col, out row)) continue;
                                OzUnitPopup.ProofHover = btn;
                                yield return Frames(2);
                                string where = Mathf.RoundToInt(s * 100) + "% " + (b.IsFort ? "fort" : b.Type) + " L" + lv + " place " + place + " " + unit;
                                Rect shot = CheckCase(where, btn, unit, col, row);
                                cases++;
                                bool lookCase = look && lv == top && place == 0;
                                if (place == 0)
                                {
                                    yield return new WaitForEndOfFrame();
                                    string name = "s" + Mathf.RoundToInt(s * 100).ToString("000", Inv) + "_" + (b.IsFort ? "fort" : b.Type) + "_L" + lv + "_" + unit;
                                    Save(shot, Path.Combine(dir, lookCase ? "look" : "cases", name + (lookCase ? ".png" : ".jpg")), lookCase, i == 0 && lv == top ? Path.Combine(dir, "frames", name + "_full.png") : null);
                                }
                            }
                            OzUnitPopup.ProofHover = null;
                        }
                    }
                    b.Level = level0;
                }
                int after = 0; for (int i = 0; i < cat.Length; i++) after += cat[i];
                index.AppendLine("- scale " + Mathf.RoundToInt(s * 100) + " %: " + (cases - cases0) + " cases, " + (after - before) + " violations");
            }

            // ---- once: it goes with the menu and with the HUD; its own frame work allocates nothing
            OzText.UiScale = scale0; hud.ApplyUiScale();
            yield return Frames(3);
            var p0 = producers[0];
            hud.Production.Close(); hud.Production.Open(p0);
            yield return Wait(0.35f);
            HudButton b0; string u0; int c0, r0; bool any = false;
            for (int i = 0; i < OzUnitPopup.TileCount && !any; i++) any = OzUnitPopup.Tile(i, out b0, out u0, out c0, out r0) && (OzUnitPopup.ProofHover = b0) != null;
            yield return Frames(2);
            var pop = OzUnitPopup.I;
            if (!any || !pop || !pop.Visible) Fail(0, "behaviour", "the card did not come up for the first portrait");
            else
            {
                long a0 = 0, a1 = 0; bool measured = true;
                try { a0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 300; i++) pop.ProofUpdate(); a1 = GC.GetAllocatedBytesForCurrentThread(); }
                catch (Exception) { measured = false; }
                index.AppendLine("- per-frame work, 300 calls with the card up: " + (measured ? (a1 - a0) + " bytes allocated" : "not measured on this runtime"));
                if (measured && a1 - a0 != 0) Fail(8, "behaviour", "the card's frame work allocated " + (a1 - a0) + " bytes in 300 calls");
                hud.Production.Close();
                yield return Frames(1);
                if (pop.Visible || pop.CardRect.gameObject.activeSelf) Fail(0, "behaviour", "the card stayed after the menu closed");
                hud.Production.Open(p0);
                yield return Wait(0.35f);
                any = false;
                for (int i = 0; i < OzUnitPopup.TileCount && !any; i++) any = OzUnitPopup.Tile(i, out b0, out u0, out c0, out r0) && (OzUnitPopup.ProofHover = b0) != null;
                yield return Frames(2);
                hud.SetVisible(false);
                yield return Frames(1);
                if (pop.Visible || pop.CardRect.gameObject.activeSelf) Fail(0, "behaviour", "the card stayed after the HUD was hidden");
                hud.SetVisible(true);
            }
            OzUnitPopup.ProofHover = null;
            hud.Production.Close();
            if (hud.rts) hud.rts.focus = focus0;
            Time.timeScale = time0;
            if (OzUnitPopup.LastResortCount != lastResort0) Fail(7, "all", "the placement rule used its last resort on " + (OzUnitPopup.LastResortCount - lastResort0) + " frames");

            int total = 0; for (int i = 0; i < cat.Length; i++) total += cat[i];
            index.AppendLine();
            index.AppendLine("## Result: " + cases + " cases, " + total + " violations, " + pictures + " pictures: " + (total == 0 ? "PASS" : "FAIL"));
            string[] names = { "A card missing or wrong unit", "B off the screen", "C over a portrait", "D over a HUD panel", "E text out of its box or shrunk", "F texts / bars touch or leave the card", "G number or line differs from the rules", "H last resort used", "I takes a click or allocates" };
            for (int i = 0; i < cat.Length; i++) index.AppendLine("- " + names[i] + ": " + cat[i]);
            if (otherRootHits.Count > 0)
            {
                index.AppendLine();
                index.AppendLine("Other HUD root nodes the card overlapped at some point (not in the fixed-panel list; look at them and decide): " + string.Join(", ", new List<string>(otherRootHits).ToArray()));
            }
            if (fails.Count > 0) { index.AppendLine(); index.AppendLine("## Violations (first " + fails.Count + ")"); foreach (var f in fails) index.AppendLine("- " + f); }
            File.WriteAllText(Path.Combine(dir, "index.md"), index.ToString());
            Debug.Log("[UnitCardProof] " + cases + " cases, " + total + " violations, " + pictures + " pictures -> " + dir + (total == 0 ? "  PASS" : "  FAIL"));
            Destroy(gameObject);
        }

        static bool Ground(Vector2 viewport, float y, out Vector3 hit)
        {
            hit = Vector3.zero;
            var ray = Camera.main.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = (y - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>All checks of one case; returns the screen rectangle to cut the picture from (menu + card).</summary>
        Rect CheckCase(string where, HudButton btn, string unit, int col, int row)
        {
            var pop = OzUnitPopup.I;
            Rect win = Px(hud.Production.WindowRect);
            if (!pop || !pop.Visible || pop.ShownUnit != unit || pop.ShownCard == null) { Fail(0, where, "card not up for this portrait (shown: " + (pop ? pop.ShownUnit : "no card") + ")"); return win; }
            float k = hud.Canvas.scaleFactor, sc = pop.CardScale;
            Rect body = Px(pop.CardRect);
            Rect card = Rect.MinMaxRect(body.xMin, body.yMin - OzUnitPopup.ShadowY * sc * k, body.xMax + OzUnitPopup.ShadowX * sc * k, body.yMax);
            Rect screen = new Rect(0, 0, Screen.width, Screen.height);
            if (!Inside(card, screen)) Fail(1, where, "card " + R(card) + " leaves the screen");
            if (pop.How == 7) Fail(7, where, "last resort place");

            // C the hovered portrait and the ones next to it
            for (int i = 0; i < OzUnitPopup.TileCount; i++)
            {
                HudButton tb; string tu; int tc, trw;
                if (!OzUnitPopup.Tile(i, out tb, out tu, out tc, out trw)) continue;
                if (Mathf.Abs(tc - col) > 1 || Mathf.Abs(trw - row) > 1) continue;
                Rect tr = Px(tb.Rt);
                if (Over(card, tr)) Fail(2, where, "card " + R(card) + " covers the portrait of " + tu + " " + R(tr));
            }
            // D the HUD panels (every active child of the HUD root; the fixed panels count, anything else is listed for a look)
            for (int i = 0; i < hud.Root.childCount; i++)
            {
                var ch = hud.Root.GetChild(i) as RectTransform;
                if (!ch || !ch.gameObject.activeInHierarchy || Array.IndexOf(SkipRoot, ch.name) >= 0) continue;
                var g = ch.GetComponent<CanvasGroup>(); if (g && g.alpha < 0.02f) continue;
                var pcv = ch.GetComponent<Canvas>(); if (pcv && !pcv.enabled) continue;       // its own canvas is off (a lower panel while the build menu is open): not on screen
                Rect pr = Px(ch);
                if (pr.width < 1f || pr.height < 1f || !Over(card, pr)) continue;
                if (Inside(Cut(card, pr), win, 1f)) continue;                               // that part of the panel is under the menu already
                bool fixedPanel = Array.IndexOf(PanelNames, ch.name) >= 0;
                foreach (var pre in PanelPrefixes) if (ch.name.StartsWith(pre, StringComparison.Ordinal)) fixedPanel = true;
                if (fixedPanel) Fail(3, where, "card " + R(card) + " covers " + ch.name + " " + R(pr));
                else if (pr.width < Screen.width * 0.8f || pr.height < Screen.height * 0.8f) otherRootHits.Add(ch.name);
            }
            // D, second part: the Zod transmission window lives on a canvas of its own that is drawn over the HUD and the menu
            var zb = ZodBriefing.I; var zc = zb ? zb.transform.Find("ZodComms") : null; var zw = zc ? zc.Find("ZodWindow") as RectTransform : null;
            if (zw && zw.gameObject.activeInHierarchy)
            {
                var zg = zw.GetComponent<CanvasGroup>(); Rect zr = Px(zw);
                if ((!zg || zg.alpha >= 0.02f) && zr.width >= 1f && zr.height >= 1f && Over(card, zr)) Fail(3, where, "card " + R(card) + " covers the Zod window " + R(zr));
            }
            // E + F the texts and the bars
            var texts = new List<OzText>(); pop.ProofTexts(texts);
            var parts = new List<Rect>(); var partNames = new List<string>();
            foreach (var t in texts)
            {
                var tmp = t.graphic as TMPro.TMP_Text;
                if (!tmp) { parts.Add(Px(t.rectTransform)); partNames.Add(t.gameObject.name); continue; }   // legacy text: its box
                tmp.ForceMeshUpdate();
                float pref = tmp.preferredWidth, box = t.rectTransform.rect.width;
                if (pref > box + 0.05f) Fail(4, where, t.gameObject.name + " \"" + Plain(t.text) + "\" is " + pref.ToString("0.0", Inv) + " u wide, box " + box.ToString("0.0", Inv) + " u");
                if (tmp.fontSize < t.Size - 0.01f || tmp.overflowMode == TMPro.TextOverflowModes.Ellipsis) Fail(4, where, t.gameObject.name + " \"" + Plain(t.text) + "\" was shrunk or cut to fit");
                var tb = tmp.textBounds; var tt = tmp.transform;
                Vector3 lo = tt.TransformPoint(tb.min), hi = tt.TransformPoint(tb.max);
                parts.Add(Rect.MinMaxRect(Mathf.Min(lo.x, hi.x), Mathf.Min(lo.y, hi.y), Mathf.Max(lo.x, hi.x), Mathf.Max(lo.y, hi.y))); partNames.Add(t.gameObject.name);
            }
            for (int i = 0; i < 3; i++) { parts.Add(Px(pop.ProofBar(i).Root)); partNames.Add("Bar" + i); }
            for (int i = 0; i < parts.Count; i++)
            {
                if (!Inside(parts[i], body, 1f)) Fail(5, where, partNames[i] + " " + R(parts[i]) + " leaves the card " + R(body));
                for (int j = i + 1; j < parts.Count; j++)
                    if (Over(parts[i], parts[j])) Fail(5, where, partNames[i] + " " + R(parts[i]) + " touches " + partNames[j] + " " + R(parts[j]));
            }
            // G the numbers and the words, by this routine's own arithmetic from the rule tables
            var S = hud.W.S; var u = S.Unit(unit); var c = pop.ShownCard;
            if (u == null) Fail(6, where, "no rule table for " + unit);
            else
            {
                int hp = (int)(u.health * ZConst.MAX_UNIT_HEALTH), dmg = (int)(u.attack_damage * ZConst.MAX_UNIT_HEALTH);
                double cyc = unit == "electrobot" ? ZElectro.CycleTicks * ZConst.TICK : u.attack_speed;
                int rad = u.attack_radius;
                if (unit == "crane" && ZWorld.CraneGunDeviation)
                {   // the crane's driver shoots back with the grunt's rifle, a crew's worth of it (ZObject.CraneGunStep)
                    var g = S.Unit("grunt");
                    if (g != null) { dmg = (int)(g.attack_damage * ZConst.MAX_UNIT_HEALTH) * ZWorld.CrewRifleMul; cyc = g.attack_speed; rad = g.attack_radius; }
                }
                bool apc = unit == "apc", armed = !apc && dmg > 0 && cyc > 0;
                string no = apc ? "as its crew" : "none";
                string eHp = hp.ToString(Inv);
                string eDmg = armed ? dmg.ToString(Inv) + " every " + cyc.ToString("0.0", Inv) + " s" : no;
                string eRange = armed && rad > 0 ? Mathf.RoundToInt(rad / 8f).ToString(Inv) + " m" : no;
                string[] exp = { eHp, eDmg, eRange };
                for (int i = 0; i < 3; i++)
                    if (Plain(pop.ProofValue(i).text) != exp[i]) Fail(6, where, "row " + i + " shows \"" + Plain(pop.ProofValue(i).text) + "\", the rules say \"" + exp[i] + "\"");
                int maxHp = 0, maxR = 0; double maxD = 0;
                foreach (var list in new[] { ZSettings.Robots, ZSettings.Vehicles, ZSettings.Cannons })
                    foreach (var nme in list)
                    {
                        var x = S.Unit(nme); if (x == null) continue;
                        int xd = (int)(x.attack_damage * ZConst.MAX_UNIT_HEALTH); double xc = nme == "electrobot" ? ZElectro.CycleTicks * ZConst.TICK : x.attack_speed;
                        maxHp = Math.Max(maxHp, (int)(x.health * ZConst.MAX_UNIT_HEALTH)); maxR = Math.Max(maxR, x.attack_radius);
                        if (nme != "apc" && xd > 0 && xc > 0) maxD = Math.Max(maxD, xd / xc);
                    }
                float[] bar = { hp / (float)maxHp, armed ? Mathf.Sqrt((float)(dmg / cyc / maxD)) : 0f, armed ? rad / (float)maxR : 0f };
                for (int i = 0; i < 3; i++)
                    if (Mathf.Abs(pop.ProofBar(i).Value - Mathf.Clamp01(bar[i])) > 0.003f) Fail(6, where, "bar " + i + " is " + pop.ProofBar(i).Value.ToString("0.000", Inv) + ", the rules give " + bar[i].ToString("0.000", Inv));
                int squad = ZBuildLists.IsRobot(unit) ? Math.Max(1, u.group_amount) : 1;
                string eTitle = UnitInfo.Name(unit) + (squad > 1 ? " ×" + squad.ToString(Inv) : "");
                if (c.title != eTitle) Fail(6, where, "title \"" + c.title + "\", expected \"" + eTitle + "\"");
                if ((squad > 1) != (c.basis.Length > 0)) Fail(6, where, "the per-robot note is " + (c.basis.Length > 0 ? "shown" : "missing") + " for a squad of " + squad);
                var w = OzUnitFacts.WordsOf(unit);
                if (w == null || w.role.Length == 0 || w.plus.Length == 0 || w.minus.Length == 0) Fail(6, where, "no words for " + unit);
                else if (c.role != w.role || c.plus != w.plus || c.minus != w.minus) Fail(6, where, "the card's lines are not the table's lines");
                foreach (var o in hud.W.Objects)                                             // a living unit of that type: the same numbers
                {
                    if (o.Type != unit || o.U == null || o.Destroyed || o.IsFortDefender || (o.Kind != ZKind.Robot && o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon)) continue;
                    if (o.MaxHealth != hp) Fail(6, where, "a living " + unit + " has " + o.MaxHealth + " HP, the card says " + hp);
                    if (!apc && (Math.Abs((double)o.Damage - dmg) > 0.5 || Math.Abs((double)o.AttackRadius - u.attack_radius) > 0.5))
                        Fail(6, where, "a living " + unit + " has damage " + o.Damage + " / radius " + o.AttackRadius + ", the card is built from " + dmg + " / " + u.attack_radius);
                    break;
                }
            }
            // I never takes a click
            foreach (var g in pop.CardRect.GetComponentsInChildren<Graphic>(true)) if (g.raycastTarget) { Fail(8, where, g.gameObject.name + " is a raycast target"); break; }
            if ((pop.How == 1 || pop.How == 2) && hud.IsOverUi(body.center)) Fail(8, where, "the HUD treats the card's place as its own (a click there would not reach the map)");
            float m = 24f;
            return Rect.MinMaxRect(Mathf.Max(0, Mathf.Min(win.xMin, card.xMin) - m), Mathf.Max(0, Mathf.Min(win.yMin, card.yMin) - m),
                                   Mathf.Min(Screen.width, Mathf.Max(win.xMax, card.xMax) + m), Mathf.Min(Screen.height, Mathf.Max(win.yMax, card.yMax) + m));
        }

        void Save(Rect r, string path, bool png, string fullPath)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                int x = Mathf.Clamp(Mathf.FloorToInt(r.xMin), 0, tex.width - 2), y = Mathf.Clamp(Mathf.FloorToInt(r.yMin), 0, tex.height - 2);
                int w = Mathf.Clamp(Mathf.CeilToInt(r.width), 2, tex.width - x), h = Mathf.Clamp(Mathf.CeilToInt(r.height), 2, tex.height - y);
                var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                crop.SetPixels(tex.GetPixels(x, y, w, h)); crop.Apply(false);
                File.WriteAllBytes(path, png ? crop.EncodeToPNG() : crop.EncodeToJPG(92));
                Destroy(crop); pictures++;
                if (fullPath != null) { File.WriteAllBytes(fullPath, tex.EncodeToPNG()); pictures++; }
            }
            finally { Destroy(tex); }
        }
    }
}
#endif
