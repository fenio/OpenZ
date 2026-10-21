using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted checks of kit KB (the owner's findings in the test exe, items 224 to 228). Each ends with one log line
    ///  * 224 UI Scale Default Table (Edit or Play): OzSettings.DefaultHudScale for ten screen sizes must be 100 % everywhere; the saved
    ///    oz_ui_scale, OzText.SavedScale and the one-time flag are printed (in Play: the live scale too).
    ///  * 225 Hero Shot Takes (Edit or Play): the clips of the one folder (OzHeroShotSound.Folder), each 0.15 .. 4 s; in Play also what
    ///    OzHeroShotSound loaded and how many hero shots played / fell back to the grunt's bolts.
    ///  * 226 Testing Overlay Ghost Paths (Play, a level, open ground in the middle of the view, the Game view in front): ten paths of
    ///    the P overlay's dragged icon through a virtual mouse of its own and the keyboard's P / Esc; after each path no icon may remain.
    ///  * 228 Dead Robot Fade Timeline (Play, a level): stages a red and a blue grunt squad near the view, kills one robot of each
    ///    through the sim's own death path, samples the dead level of the body (TeamPaint.DeadLevel) and of the thrown pieces (_OZDead)
    ///    at +0.1 .. +3.5 s, and takes two pictures (death +0 and +3 s) beside the live squad mates.
    /// </summary>
    public static class OzV11KbExeFindingsCheck
    {
        const string Tag = "[OpenZ V1.1 KB]";
        public static string Report = "not run";
        static string Dir => OzRelease.Milestones("v1_1_20261012/kb_exe_findings");

        static void Save(string name, string text)
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, name), text + "\n"); }
            catch (System.Exception e) { Debug.LogWarning(Tag + " could not write " + name + ": " + e.Message); }
        }

        static void Shot(string name)
        {
            try { Directory.CreateDirectory(Dir); ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name)); }
            catch (System.Exception e) { Debug.LogWarning(Tag + " no picture " + name + ": " + e.Message); }
        }

        sealed class Runner : MonoBehaviour { }
        static Runner runner;
        static Runner R()
        {
            if (!runner) { var go = new GameObject("OzKbCheckRunner") { hideFlags = HideFlags.HideAndDontSave }; runner = go.AddComponent<Runner>(); }
            return runner;
        }
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        // ------------------------------------------------------------------ 224
        [MenuItem("OpenZ/V1.1/KB 224 UI Scale Default Table (Edit or Play)")]
        public static void ScaleTable()
        {
            var sizes = new[] { (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (1920, 1200), (2560, 1440), (2560, 1600), (2880, 1800), (3440, 1440), (3840, 2160) };
            var sb = new StringBuilder(); int ok = 0;
            foreach (var (w, h) in sizes)
            {
                float d = OzSettings.DefaultHudScale(w, h);
                if (Mathf.Abs(d - 1f) < 1e-4f) ok++;
                sb.Append($" {w}x{h} -> {Mathf.RoundToInt(d * 100f)} %;");
            }
            bool saved = PlayerPrefs.HasKey("oz_ui_scale");
            string live = OpenZHud.I ? $", live UI scale {Mathf.RoundToInt(OzText.UiScale * 100f)} % at {Screen.width}x{Screen.height}" : "";
            string line = $"{Tag} 224 default HUD scale: {(ok == sizes.Length ? "PASS" : "FAIL")} {ok}/{sizes.Length} sizes at 100 %:{sb} saved oz_ui_scale " +
                          $"{(saved ? Mathf.RoundToInt(PlayerPrefs.GetFloat("oz_ui_scale", 1f) * 100f) + " %" : "none")}, OzText.SavedScale {Mathf.RoundToInt(OzText.SavedScale * 100f)} %, " +
                          $"one-time flag {OzSettings.HudScalePref} = {PlayerPrefs.GetInt(OzSettings.HudScalePref, 0)}{live}";
            Report = line; Debug.Log(line); Save("224_scale_table.txt", line);
        }

        // ------------------------------------------------------------------ 225
        [MenuItem("OpenZ/V1.1/KB 225 Hero Shot Takes (Edit or Play)")]
        public static void HeroShotTakes()
        {
            var clips = Resources.LoadAll<AudioClip>(OzHeroShotSound.Folder);
            var sb = new StringBuilder(); int good = 0;
            foreach (var c in clips.OrderBy(x => x.name))
            {
                bool fine = c.length >= 0.15f && c.length <= 4f && c.frequency >= 22050;
                if (fine) good++;
                sb.Append($" {c.name} {c.length:0.00} s {c.frequency} Hz {c.channels} ch {c.loadType}{(fine ? "" : " (ODD)")};");
            }
            string play = "";
            if (Application.isPlaying)
            {
                OzHeroShotSound.Load();
                play = $" | in Play: {OzHeroShotSound.Status}; event plays {OzHeroShotSound.Played}, grunt-bolt fallbacks {OzHeroShotSound.Fallbacks}, hero shots counted {OzHeroSquadView.ShotsPlayed}";
            }
            bool pass = clips.Length > 0 && good == clips.Length;
            string line = $"{Tag} 225 hero shot takes: {(pass ? "PASS" : "FAIL")} {good}/{clips.Length} clips in Resources/{OzHeroShotSound.Folder} (the one folder; event \"{OzHeroShotSound.Key}\"):{sb}{play}";
            Report = line; Debug.Log(line); Save("225_hero_shot_takes.txt", line);
        }

        // ------------------------------------------------------------------ 226
        [MenuItem("OpenZ/V1.1/KB 226 Testing Overlay Ghost Paths (Play)")]
        public static void GhostPaths()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a level)"); return; }
            var hud = OpenZHud.I;
            if (!hud || !hud.Root || ZGame.I == null || ZGame.I.World == null) { Report = Tag + " 226 FAIL: no HUD / game in this scene (a level is needed)"; Debug.LogError(Report); return; }
            if (!OzCheatSpawner.DevEnabled || !OzCheatSpawner.I) { Report = Tag + " 226 FAIL: the testing overlay is not installed (oz_dev 0?)"; Debug.LogError(Report); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            Report = "running"; R().StartCoroutine(GhostRoutine(hud));
        }

        static IEnumerator GhostRoutine(OpenZHud hud)
        {
            int ok = 0, bad = 0; var notes = new StringBuilder();
            void Check(bool pass, string what) { if (pass) ok++; else bad++; notes.Append(pass ? " [ok] " : " [FAIL] ").Append(what).Append(';'); }
            var game = ZGame.I; var kb = Keyboard.current;
            Mouse vm = null, vm2 = null;
            try { vm = InputSystem.AddDevice<Mouse>("OzKbMouse"); }
            catch (System.Exception e) { Report = Tag + " 226 FAIL: no virtual mouse: " + e.Message; Debug.LogError(Report); yield break; }
            void MouseAt(Mouse m, Vector2 p, bool left, bool right = false)
            {
                var st = new MouseState { position = p };
                if (left) st = st.WithButton(MouseButton.Left);
                if (right) st = st.WithButton(MouseButton.Right);
                InputSystem.QueueStateEvent(m, st);
            }
            IEnumerator KeyTap(Key k)
            {
                InputSystem.QueueStateEvent(kb, new KeyboardState(k)); yield return Frames(2);
                InputSystem.QueueStateEvent(kb, new KeyboardState()); yield return Frames(2);
            }
            Vector2 Centre(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            Vector2 mapDrop = new Vector2(Screen.width * 0.5f, Screen.height * 0.55f);
            try
            {
                if (OzCheatSpawner.Visible) yield return KeyTap(Key.P);
                MouseAt(vm, mapDrop, false); yield return Frames(2);
                // 1: P shows the overlay with no icon on the screen (before the kit a blank 60 px ghost sat at the canvas corner on the first show)
                yield return KeyTap(Key.P);
                Check(OzCheatSpawner.Visible && !OzCheatSpawner.GhostVisible, $"1 P shows the overlay, no icon on the screen (shown {OzCheatSpawner.Visible}, icon {OzCheatSpawner.GhostVisible})");
                var tile = hud.Root.Find("CheatRed/Cell_tough") as RectTransform; var panel = hud.Root.Find("CheatBlue") as RectTransform;
                if (!tile || !panel) Check(false, "the overlay's tile CheatRed/Cell_tough or the panel CheatBlue was not found under the HUD root");
                else
                {
                    Vector2 c = Centre(tile);
                    IEnumerator DragTo(Vector2 to)
                    {
                        MouseAt(vm, c, false); yield return Frames(2); MouseAt(vm, c, true); yield return Frames(2);
                        for (int k = 1; k <= 6; k++) { MouseAt(vm, Vector2.Lerp(c, to, k / 6f), true); yield return Frames(1); }
                    }
                    // 2: drag to the map and drop
                    int n0 = game.World.Objects.Count;
                    yield return DragTo(mapDrop); bool during = OzCheatSpawner.GhostVisible; MouseAt(vm, mapDrop, false); yield return Frames(3);
                    int made = game.World.Objects.Count - n0;
                    Check(during && !OzCheatSpawner.GhostVisible && made > 0, $"2 drag to the map and drop: the icon followed the drag ({during}), spawned +{made}, no icon left ({!OzCheatSpawner.GhostVisible})");
                    // 3: P off then P on after a drop: the owner's stuck icon
                    yield return KeyTap(Key.P); bool hidClean = !OzCheatSpawner.Visible && !OzCheatSpawner.GhostVisible; yield return KeyTap(Key.P);
                    Check(hidClean && OzCheatSpawner.Visible && !OzCheatSpawner.GhostVisible, $"3 P off then P on after a drop: no icon comes back (hidden clean {hidClean}, shown again {OzCheatSpawner.Visible}, icon {OzCheatSpawner.GhostVisible})");
                    // 4: drop back on a panel
                    n0 = game.World.Objects.Count; var pc = Centre(panel);
                    yield return DragTo(pc); MouseAt(vm, pc, false); yield return Frames(3);
                    Check(!OzCheatSpawner.GhostVisible && game.World.Objects.Count == n0, $"4 drop back on a panel: nothing spawned (+{game.World.Objects.Count - n0}), no icon left ({!OzCheatSpawner.GhostVisible})");
                    // 5: Esc mid-drag
                    yield return DragTo(mapDrop); yield return KeyTap(Key.Escape); MouseAt(vm, mapDrop, false); yield return Frames(2);
                    Check(!OzCheatSpawner.GhostVisible && game.World.Objects.Count == n0, $"5 Esc mid-drag: cancelled, no icon left ({!OzCheatSpawner.GhostVisible}), nothing spawned (+{game.World.Objects.Count - n0})");
                    // 6: right click mid-drag
                    yield return DragTo(mapDrop); MouseAt(vm, mapDrop, true, true); yield return Frames(2); MouseAt(vm, mapDrop, false); yield return Frames(2);
                    Check(!OzCheatSpawner.GhostVisible && game.World.Objects.Count == n0, $"6 right click mid-drag: cancelled, no icon left ({!OzCheatSpawner.GhostVisible}), nothing spawned (+{game.World.Objects.Count - n0})");
                    // 7: P mid-drag hides the overlay, P shows it again
                    yield return DragTo(mapDrop); yield return KeyTap(Key.P); bool gone = !OzCheatSpawner.GhostVisible && !OzCheatSpawner.Visible;
                    MouseAt(vm, mapDrop, false); yield return Frames(2); yield return KeyTap(Key.P);
                    Check(gone && OzCheatSpawner.Visible && !OzCheatSpawner.GhostVisible && game.World.Objects.Count == n0, $"7 P mid-drag then P again: the icon went with the overlay ({gone}), none when it shows again ({!OzCheatSpawner.GhostVisible}), nothing spawned");
                    // 8: the window loses focus mid-drag (the component's own focus message, called directly)
                    yield return DragTo(mapDrop); OzCheatSpawner.I.CancelDrag(); yield return Frames(1); MouseAt(vm, mapDrop, false); yield return Frames(2);
                    Check(!OzCheatSpawner.GhostVisible && game.World.Objects.Count == n0, $"8 focus lost mid-drag: no icon left ({!OzCheatSpawner.GhostVisible}), nothing spawned (+{game.World.Objects.Count - n0})");
                    // 9: the release is never seen: another mouse with its button up becomes the current one mid-drag (no release event at all)
                    yield return DragTo(mapDrop);
                    vm2 = InputSystem.AddDevice<Mouse>("OzKbMouse2"); MouseAt(vm2, mapDrop, false); yield return Frames(3);
                    Check(!OzCheatSpawner.GhostVisible && game.World.Objects.Count == n0, $"9 the release never arrives (the button is up on the current mouse, no release event): the drag is dropped, no icon left ({!OzCheatSpawner.GhostVisible}), nothing spawned");
                    InputSystem.RemoveDevice(vm2); vm2 = null; MouseAt(vm, mapDrop, false); yield return Frames(2);
                    // 10: two drags back to back
                    n0 = game.World.Objects.Count;
                    yield return DragTo(mapDrop); MouseAt(vm, mapDrop, false); yield return Frames(2);
                    var drop2 = mapDrop + new Vector2(140f, 0f);
                    yield return DragTo(drop2); MouseAt(vm, drop2, false); yield return Frames(3);
                    Check(!OzCheatSpawner.GhostVisible && game.World.Objects.Count > n0, $"10 two drags back to back: spawned +{game.World.Objects.Count - n0}, no icon left ({!OzCheatSpawner.GhostVisible})");
                }
                if (OzCheatSpawner.Visible) yield return KeyTap(Key.P);
                Check(!OzCheatSpawner.Visible && !OzCheatSpawner.GhostVisible, "11 the overlay hidden at the end, no icon on the screen");
            }
            finally
            {
                if (vm2 != null) InputSystem.RemoveDevice(vm2);
                if (vm != null) InputSystem.RemoveDevice(vm);
            }
            int n = ok + bad;
            Report = $"{Tag} 226 testing overlay icon: {(bad == 0 ? "PASS" : "FAIL")} {ok}/{n} paths leave no icon on the screen (drags started {OzCheatSpawner.Drags});{notes}";
            if (bad == 0) Debug.Log(Report); else Debug.LogError(Report);
            Save("226_ghost_paths.txt", Report);
        }

        // ------------------------------------------------------------------ 228
        [MenuItem("OpenZ/V1.1/KB 228 Dead Robot Fade Timeline (Play)")]
        public static void DeadFade()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a level)"); return; }
            if (ZGame.I == null || ZGame.I.World == null) { Report = Tag + " 228 FAIL: no game (a level is needed)"; Debug.LogError(Report); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            Report = "running"; R().StartCoroutine(DeadRoutine());
        }

        static bool Free(ZWorld W, int cx, int cy, out int tx, out int ty)
        {
            for (int r = 0; r <= 8; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (W.Path.TilePassable(x, y, true) && W.Path.TilePassable(x + 1, y, true) && W.Path.TilePassable(x, y + 1, true)) { tx = x; ty = y; return true; }
                    }
            tx = cx; ty = cy; return false;
        }

        static IEnumerator DeadRoutine()
        {
            var g = ZGame.I; var W = g.World; var hud = OpenZHud.I; var rts = hud ? hud.rts : null;
            int ok = 0, bad = 0; var notes = new StringBuilder();
            void Check(bool pass, string what) { if (pass) ok++; else bad++; notes.Append(pass ? " [ok] " : " [FAIL] ").Append(what).Append(';'); }
            Vector3 f = rts ? rts.focus : Vector3.zero; var px = ZGame.WorldToPx(f);
            var staged = new List<(Team team, ZObject squad)>();
            foreach (var (team, dx) in new[] { (Team.Red, -5), (Team.Blue, 5) })
            {
                if (!Free(W, (px.x >> 4) + dx, px.y >> 4, out int tx, out int ty)) { Check(false, $"no open ground for the {team} squad near the view"); continue; }
                var o = W.CreateRobotGroup("grunt", tx * 16, ty * 16, team);
                if (o == null) { Check(false, $"CreateRobotGroup gave nothing for {team}"); continue; }
                g.SpawnUnitView(o); foreach (var mm in o.Minions) g.SpawnUnitView(mm);
                staged.Add((team, o));
            }
            OzHighScores.Unranked = "KB CHECK STAGED UNITS · NOT IN THE TABLE";
            yield return Frames(30);
            if (rts) { rts.FreeLookOff(); rts.focus = new Vector3(f.x, 0f, f.z); rts.SetHeightImmediate(70f); }
            yield return Frames(10);
            var victims = new List<(Team team, ZObject o, UnitView v)>();
            var views = Object.FindObjectsByType<UnitView>(FindObjectsSortMode.None);
            foreach (var (team, squad) in staged)
            {
                var victim = squad.Minions != null ? squad.Minions.FirstOrDefault() ?? squad : squad;
                var view = views.FirstOrDefault(x => x && x.Obj == victim);
                victims.Add((team, victim, view));
            }
            Check(victims.Count == 2, $"a red and a blue grunt squad staged and one robot of each picked ({victims.Count} of 2)");
            int parts0 = OzDeadTint.PartsTinted, pops0 = RobotPopDeath.Pops;
            foreach (var (team, o, v) in victims) { o.DamageHealth(o.Health + 1); W.UpdateObjectHealth(o, -1); }
            float t0 = Time.time;
            yield return null;
            Shot("228_death_plus0.png");
            var table = new StringBuilder(); var mpb = new MaterialPropertyBlock();
            float[] at = { 0.1f, 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f };
            var samples = new List<(float t, float body, float piece)>();
            foreach (float tt in at)
            {
                while (Time.time - t0 < tt) yield return null;
                float body = -1f, piece = -1f;
                foreach (var (team, o, v) in victims) if (v) { var tp = v.GetComponent<TeamPaint>(); if (tp && tp.GetComponent<OzGlowFade>()) body = Mathf.Max(body, tp.DeadLevel); }
                foreach (var pr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!pr || pr.gameObject.name != "OZ_PopPiece" || !pr.gameObject.activeInHierarchy) continue;
                    pr.GetPropertyBlock(mpb); if (mpb.isEmpty) pr.GetPropertyBlock(mpb, 0);
                    piece = Mathf.Max(piece, mpb.GetFloat(OzDeadTint.DeadId));
                }
                samples.Add((tt, body, piece));
                table.Append($" +{tt:0.0} s: body {(body < 0f ? "-" : body.ToString("0.00"))}, pieces {(piece < 0f ? "-" : piece.ToString("0.00"))};");
                if (Mathf.Abs(tt - 3.0f) < 0.01f) Shot("228_death_plus3.png");
            }
            bool anyBody = samples.Any(s => s.body >= 0f), anyPiece = samples.Any(s => s.piece >= 0f);
            Check(anyBody || anyPiece, $"something to measure after the kills: bodies {anyBody}, thrown pieces {anyPiece} (pop deaths {RobotPopDeath.Pops - pops0}, parts given the fade {OzDeadTint.PartsTinted - parts0})");
            float Lv(int i, bool bodyKind) => bodyKind ? samples[i].body : samples[i].piece;
            foreach (bool kind in new[] { true, false })
            {
                if (!(kind ? anyBody : anyPiece)) continue;
                string k = kind ? "body" : "pieces";
                float s0 = Lv(0, kind), s2 = Lv(2, kind), s6 = Lv(6, kind);
                Check(s0 >= 0f && s0 <= 0.1f, $"{k} at +0.1 s still live (dead level {s0:0.00} <= 0.10)");
                Check(s2 >= 0.25f && s2 <= 0.6f, $"{k} at +1.0 s half way (dead level {s2:0.00} in 0.25 .. 0.60)");
                Check(s6 >= 0.95f, $"{k} at +3.0 s fully dead-coloured (dead level {s6:0.00} >= 0.95)");
                bool mono = true; for (int i = 1; i < samples.Count; i++) if (Lv(i, kind) >= 0f && Lv(i - 1, kind) >= 0f && Lv(i, kind) < Lv(i - 1, kind) - 1e-3f) mono = false;
                Check(mono, $"{k} never turns back towards the live colour");
            }
            int n = ok + bad;
            Report = $"{Tag} 228 dead robot fade: {(bad == 0 ? "PASS" : "FAIL")} {ok}/{n} (OzDeadTint.Seconds {OzDeadTint.Seconds} s, {OzDeadTint.Steps} steps); timeline:{table}{notes} pictures: {Dir}/228_death_plus0.png and 228_death_plus3.png";
            if (bad == 0) Debug.Log(Report); else Debug.LogError(Report);
            Save("228_dead_fade.txt", Report);
        }
    }
}
