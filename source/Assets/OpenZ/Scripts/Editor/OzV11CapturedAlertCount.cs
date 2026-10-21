using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// no reminder ("!" chip, amber ring, "CLICK TO SET PRODUCTION &amp; RALLY POINT"); one taken on its default production still gets it.
    /// Menu: OpenZ/V1.1/K429b Captured Factory Alert Check (Play mode), or OpenZ.EditorTools.OzV11CapturedAlertCount.Run().
    /// Play mode in a level with at least one zone with a robot or vehicle factory the player does not own (Level 2 is fine: it also has
    /// neutral ones), a running match, no menu open, the Game view in front. The CHECK CHANGES THE MATCH (it hands zones to the computer
    /// and to the player the way a flag capture does, ZWorld.AwardZone): restart the level afterwards.
    /// It ends with one log line "[OpenZ V1.1 K429b captured alert] PASS n/n checks (...)" or "... FAIL ..." (also in Report and in
    /// picture per case at the standard view height, the factory in the middle.
    ///  C1  the computer takes zone Z, chooses another unit for its factory (STOP then START, as ZBot does), the player takes Z:
    ///      a) the factory goes on building that unit, b) no mark, no tip, c) counted as kept (KeptChoice +1, Marked +0)
    ///  C2  the computer takes Z back and runs the default unit, the player takes Z: a) the mark and the tip "CLICK TO SET PRODUCTION &amp;
    ///      RALLY POINT", b) the badge draws it (the overlay's reminder frames grow)
    ///  C3  a neutral zone with a factory (a factory new to the player): the rules start its default production, the mark and the tip
    ///  C4  CapturedChoiceCounts off: C1's capture again gives the mark (the old way); the switch is on again afterwards
    /// </summary>
    public static class OzV11CapturedAlertCount
    {
        const string Tag = "[OpenZ V1.1 K429b captured alert]";
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        /// <summary>The view's height for the pictures (the standard RTS height).</summary>
        public static float ViewHeight = 70f;

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame, ok, bad;
        static float waitUntil;
        static readonly StringBuilder fails = new StringBuilder();
        static string dir;

        [MenuItem("OpenZ/V1.1/K429b Captured Factory Alert Check (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a level)"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var hud = OpenZHud.I;
            if (!hud || hud.W == null) { Report = Tag + " FAIL: no HUD in this scene (a level is needed)"; Debug.LogError(Report); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            ok = bad = 0; fails.Clear(); Report = "running";
            dir = OutDir(); Directory.CreateDirectory(dir);
            stack.Push(Level(hud));
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        static string OutDir()
        {
            if (!string.IsNullOrEmpty(Folder)) return Path.GetFullPath(Folder);
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "k429b_captured_alert");
        }

        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { stack.Clear(); PutBack(); EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); stack.Clear(); PutBack(); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); EditorApplication.update -= Step; return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) { PutBack(); EditorApplication.update -= Step; } return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);
        }

        static void PutBack() { OzFactoryReminder.CapturedChoiceCounts = true; }

        static void Check(bool pass, string what) { if (pass) ok++; else { bad++; fails.Append(" | ").Append(what); } Debug.Log($"{Tag} {(pass ? "ok  " : "FAIL")} {what}"); }

        static void Finish(string where, string detail)
        {
            PutBack();
            bool pass = bad == 0 && ok > 0;
            Report = $"{Tag} {(pass ? "PASS" : "FAIL")} {ok}/{ok + bad} checks ({where}): {detail}{(bad == 0 ? "" : "; FAILED:" + fails)}";
            if (pass) Debug.Log(Report); else Debug.LogError(Report);
            File.AppendAllText(Path.Combine(dir, "checks.txt"), Report + "\n");
        }

        // ------------------------------------------------------------------ helpers
        static ZObject FactoryOf(ZObject flag) => flag.LinkedObjects.FirstOrDefault(b => OzFactoryReminder.Eligible(b) && !b.Destroyed);

        /// <summary>A unit of the factory's list at its level that is not the default (a robot or vehicle first, else a cannon).</summary>
        static string OtherUnit(ZObject f)
        {
            var list = ZBuildLists.For(f.Type, f.BuildLevel); string def = OzFactoryReminder.DefaultUnit(f), cannon = null;
            foreach (var u in list)
            {
                if (u == def) continue;
                if (!ZBuildLists.IsCannon(u)) return u;
                if (cannon == null) cannon = u;
            }
            return cannon;
        }

        static IEnumerator Look(OpenZHud hud, ZObject f)
        {
            hud.rts.focus = hud.game.CenterWorld(f); hud.rts.SetHeightImmediate(ViewHeight);
            yield return 0.3f; yield return 4;
        }

        static string wanted;

        /// <summary>The computer takes the zone; a few frames later (the overlay has seen it change hands) it sets the production
        /// (another unit than the default when chosen, else the default unit; read at its level then: `wanted`) and the player takes the
        /// zone in the same step, before any rule tick or computer round can change it.</summary>
        static IEnumerator Handover(OpenZHud hud, ZObject flag, ZObject f, Team cpu, bool chosen)
        {
            var W = hud.W;
            if (flag.Owner != cpu) W.AwardZone(flag, cpu);
            yield return 0.3f; yield return 4;
            wanted = chosen ? OtherUnit(f) : OzFactoryReminder.DefaultUnit(f);
            if (wanted != null && (f.CurUnit != wanted || f.Queue.Count > 0)) { W.StopBuilding(cpu, f); W.StartBuilding(cpu, f, wanted); }
            W.AwardZone(flag, hud.Player);
            yield return 0.5f; yield return 6;
        }

        // ------------------------------------------------------------------ the run
        static IEnumerator Level(OpenZHud hud)
        {
            var W = hud.W; var me = hud.Player; var cpu = me == Team.Red ? Team.Blue : Team.Red;
            var ovl = Object.FindFirstObjectByType<OpenZUnitOverlay>();
            if (hud.Modal || W.GameOver || !hud.rts || !hud.hudVisible || !ovl || !ovl.buildingTimers || !OzFactoryReminder.Enabled)
            { Report = Tag + " FAIL: a running match with the HUD and the building badges shown, no menu open, is needed"; Debug.LogError(Report); yield break; }
            var zones = W.Flags.Where(fl => fl.Owner != me && FactoryOf(fl) != null).ToList();
            var neutral = zones.FirstOrDefault(fl => fl.Owner == Team.Neutral);
            var z = zones.FirstOrDefault(fl => fl != neutral) ?? neutral;
            if (z == null) { Report = Tag + " FAIL: no zone with a robot or vehicle factory the player does not own (try Level 2)"; Debug.LogError(Report); yield break; }
            if (z == neutral) neutral = null;
            var f = FactoryOf(z);
            Debug.Log($"{Tag} zone of flag #{z.Id} ({z.Owner}) with {f.Type}#{f.Id}; neutral zone for C3: {(neutral != null ? "flag #" + neutral.Id : "none")}. The check changes the match: restart the level afterwards.");
            yield return Look(hud, f);

            // C1: the computer chose another unit
            int k0 = OzFactoryReminder.KeptChoice, m0 = OzFactoryReminder.Marked;
            yield return Handover(hud, z, f, cpu, true);
            string alt = wanted, def = OzFactoryReminder.DefaultUnit(f);
            Check(alt != null && f.Owner == me && f.CurUnit == alt && f.Building, $"C1a the factory goes on with the computer's choice: {f.CurUnit ?? "nothing"} (chosen {alt ?? "none"}, default {def})");
            Check(!OzFactoryReminder.Active(f, me) && OzFactoryReminder.Tip(f, me) == null, $"C1b no mark and no tip on {f.Type}#{f.Id}");
            Check(OzFactoryReminder.KeptChoice - k0 == 1 && OzFactoryReminder.Marked - m0 == 0, $"C1c counted as kept: KeptChoice +{OzFactoryReminder.KeptChoice - k0}, Marked +{OzFactoryReminder.Marked - m0}");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "k429b_c1_changed_production_no_alert.png"));
            yield return 0.3f; yield return 3;

            // C2: the computer runs the default unit
            m0 = OzFactoryReminder.Marked;
            yield return Handover(hud, z, f, cpu, false);
            int fr0 = OpenZUnitOverlay.ReminderBadgeFrames;
            yield return 0.3f; yield return 6;
            Check(f.Owner == me && f.CurUnit == def && OzFactoryReminder.Active(f, me) && OzFactoryReminder.Tip(f, me) == OzFactoryReminder.TipBoth && OzFactoryReminder.Marked - m0 == 1,
                $"C2a taken on its default production ({f.CurUnit}): the mark, tip \"{OzFactoryReminder.Tip(f, me)}\"");
            Check(OpenZUnitOverlay.ReminderBadgeFrames > fr0, $"C2b the badge draws the mark ({OpenZUnitOverlay.ReminderBadgeFrames - fr0} badge frames)");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "k429b_c2_default_production_alert.png"));
            yield return 0.3f; yield return 3;

            // C3: a factory new to the player (a neutral zone)
            if (neutral != null)
            {
                var g = FactoryOf(neutral);
                yield return Look(hud, g);
                m0 = OzFactoryReminder.Marked;
                W.AwardZone(neutral, me);
                yield return 0.5f; yield return 6;
                Check(g.Owner == me && g.CurUnit == OzFactoryReminder.DefaultUnit(g) && OzFactoryReminder.Active(g, me) && OzFactoryReminder.Tip(g, me) == OzFactoryReminder.TipBoth && OzFactoryReminder.Marked - m0 == 1,
                    $"C3 a neutral {g.Type}#{g.Id} taken: the rules start {g.CurUnit ?? "nothing"} (its default), the mark, tip \"{OzFactoryReminder.Tip(g, me)}\"");
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "k429b_c3_new_factory_alert.png"));
                yield return 0.3f; yield return 3;
                yield return Look(hud, f);
            }
            else Debug.Log($"{Tag} C3 skipped: no neutral zone with a factory in this match (Level 2 has them)");

            // C4: the switch off = the old way
            OzFactoryReminder.CapturedChoiceCounts = false;
            m0 = OzFactoryReminder.Marked; k0 = OzFactoryReminder.KeptChoice;
            yield return Handover(hud, z, f, cpu, true);
            bool oldWay = f.CurUnit == wanted && wanted != OzFactoryReminder.DefaultUnit(f) && OzFactoryReminder.Active(f, me) && OzFactoryReminder.Marked - m0 == 1 && OzFactoryReminder.KeptChoice == k0;
            OzFactoryReminder.CapturedChoiceCounts = true;
            Check(oldWay && OzFactoryReminder.CapturedChoiceCounts, $"C4 CapturedChoiceCounts off: the changed factory ({f.CurUnit}) taken gets the mark (the old way); the switch is on again");

            Finish("level " + hud.game.level, $"{f.Type}#{f.Id} in the zone of flag #{z.Id}; chosen {alt}, default {def}; kept choices {OzFactoryReminder.KeptChoice}, marks given {OzFactoryReminder.Marked}; the match was changed: restart the level");
        }
    }
}
