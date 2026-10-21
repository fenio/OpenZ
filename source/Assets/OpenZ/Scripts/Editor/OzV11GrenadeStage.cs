using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Staging helper for the pictures of kit b31_grenades: how big the blast of a thrown grenade is drawn, beside the blasts it
    /// must be judged against. Editor only, Play mode, in a running level. Menu: OpenZ/V1.1/Stage Grenade Blasts For Pictures
    /// (b31, Play mode), or OpenZ.EditorTools.OzV11GrenadeStage.Run().
    ///
    /// On open ground the helper looks for itself (plain ground or road, clear of every object, as far from every unit as the
    /// level allows) four blasts go off in the same frame, in a row from west to east, 9 m apart:
    ///   1  a grenade as it was drawn until now (the blast table's Grenade row: strength 0.75, glow radius 3.0 m)
    ///   2  a grenade as it is drawn now, for the radius the rules give a thrown grenade at this moment (View/OzGrenadeBlast.cs),
    ///      through the game's own path for a landing grenade, so its sound plays and is in the sound log
    ///   3  a Tough's rocket (the table's row, strength 1.0)
    ///   4  a light tank's shell (the table's row, strength 0.95)
    /// Blasts 1, 3 and 4 are drawn without sound, so the only blast sound in the log is the grenade's. Nothing of the rules is
    /// touched: no missile, no damage, no unit; rockets and shells are shown in their usual look (their second explosion look,
    /// which the game draws on some hits, is left out so that the four can be compared).
    /// Two rows are staged, one after the other: the first clean, the second 14 m further north with a thin white circle on the
    /// ground at each blast's REAL radius (30 px, the thrown grenade's radius now, 40 px, 40 px). Three pictures per row from the
    /// normal game camera (height 70): 0.22 s after the blasts (the fire at its largest), 0.5 s (the shock ring out), 2.6 s (the
    /// burn marks). The file names carry "day" or "night" by the level's own night value: run it once by day and once by night.
    ///
    /// It writes on_screen.txt (the place, the time of day, the four blasts' numbers, the sound log lines of the grenade's blast)
    /// b31_grenades). No setting is changed and the camera goes back to where it was. The burn marks stay until they fade.
    /// </summary>
    public static class OzV11GrenadeStage
    {
        const string Tag = "[OpenZ grenade blasts staged]";
        const string RunKey = "OpenZ.V11.GrenadeStage.startedAt";
        const float MaxSeconds = 120f, CamHeight = 70f, Apart = 9f, RowApart = 14f;
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        /// <summary>The tile under the middle of the first row. Under 0 (default) = the helper looks for open ground itself.</summary>
        public static int TileX = -1, TileY = -1;

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static readonly List<GameObject> marks = new List<GameObject>();
        static int waitFrame;
        static float waitUntil, height0;
        static Vector3 focus0;
        static bool camKept;
        static string dir;

        [MenuItem("OpenZ/V1.1/Stage Grenade Blasts For Pictures (b31, Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a running level)"); return; }
            float started = SessionState.GetFloat(RunKey, -1f);
            if (stack.Count > 0 || (started >= 0f && EditorApplication.timeSinceStartup - started < MaxSeconds)) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var g = ZGame.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            string no = !g || g.World == null ? "no level is running" : !rts ? "no game camera" : g.paused ? "the game is paused: close the menu first" : null;
            if (no != null) { Report = Tag + " NOT RUN: " + no; Debug.LogError(Report); return; }
            SessionState.SetFloat(RunKey, (float)EditorApplication.timeSinceStartup);
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            dir = OutDir(); Directory.CreateDirectory(Path.Combine(dir, "for_owner"));
            Report = "running";
            stack.Push(Stage(g, rts));
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
            return Path.Combine(all, stage, "b31_grenades");
        }

        // a step yields an int (frames), a float (seconds of real time) or another step (run to its end first)
        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { End(); return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); Report = Tag + " STOPPED: exception " + ex.Message; Debug.LogError(Report); End(); return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) End(); return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);
        }

        /// <summary>Every way out: the helper's circles are gone, the camera is back, the next start is free.</summary>
        static void End()
        {
            stack.Clear(); EditorApplication.update -= Step; SessionState.EraseFloat(RunKey);
            foreach (var m in marks) if (m) Object.Destroy(m);
            marks.Clear();
            var rts = Application.isPlaying ? Object.FindFirstObjectByType<RtsCamera>() : null;
            if (camKept && rts) { rts.focus = focus0; rts.SetHeightImmediate(height0); }
            camKept = false;
        }

        // ------------------------------------------------------------------ the place
        /// <summary>The top-left tile of an open box of 24 x 16 tiles (48 m x 32 m): plain ground or road that units may stand on,
        /// nothing of the level in it or within 2 tiles of it; of all such boxes the one farthest from every robot, vehicle and gun.</summary>
        static bool FindPlace(ZWorld W, out int bx, out int by, out float far)
        {
            int w = W.Map.width, h = W.Map.height; const int BW = 24, BH = 16;
            var taken = new bool[w, h];
            foreach (var o in W.Objects)
            {
                if (o.KillAt >= 0) continue;
                int x0 = o.LocX >> 4, y0 = o.LocY >> 4, x1 = (o.LocX + Mathf.Max(16, o.WidthPix) - 1) >> 4, y1 = (o.LocY + Mathf.Max(16, o.HeightPix) - 1) >> 4;
                for (int y = y0 - 2; y <= y1 + 2; y++)
                    for (int x = x0 - 2; x <= x1 + 2; x++)
                        if (x >= 0 && y >= 0 && x < w && y < h) taken[x, y] = true;
            }
            var open = new bool[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = W.Map.ClassAt(x, y);
                    open[x, y] = (c == ZClass.Ground || c == ZClass.Road) && !taken[x, y] && W.Path.TilePassable(x, y, false) && W.Path.TilePassable(x, y, true);
                }
            bx = by = -1; far = -1f;
            for (int y = 2; y + BH < h - 2; y++)
                for (int x = 2; x + BW < w - 2; x++)
                {
                    bool ok = true;
                    for (int yy = y; yy < y + BH && ok; yy++) for (int xx = x; xx < x + BW && ok; xx++) ok = open[xx, yy];
                    if (!ok) continue;
                    float cx = (x + BW * 0.5f) * 16f, cy = (y + BH * 0.5f) * 16f, near = float.MaxValue;
                    foreach (var o in W.Objects)
                    {
                        if (!o.IsUnit || o.KillAt >= 0) continue;
                        float d = Mathf.Sqrt((o.CenterX - cx) * (o.CenterX - cx) + (o.CenterY - cy) * (o.CenterY - cy));
                        if (d < near) near = d;
                    }
                    if (near > far) { far = near; bx = x; by = y; }
                }
            return bx >= 0;
        }

        // ------------------------------------------------------------------ the blasts
        /// <summary>How far from its centre the fire of a table blast of strength s reaches when the fireballs are at their largest
        /// (the blast recipe of View/OpenZFxCatalog.cs M12Fire: 5 fireballs born 0.38 m out, flying at 1.6 m/s for 0.45 + 0.07 j s,
        /// (0.8 + 0.12 j) x s x 2.6 m wide, largest at 0.44 of their life at 0.81 of that).</summary>
        public static float FireEdge(float s)
        {
            float best = 0f, peak = Mathf.Sin(Mathf.PI * 0.44f) * (1f - 0.4f * 0.44f);
            for (int j = 0; j < 5; j++) best = Mathf.Max(best, 0.38f + 1.6f * 0.44f * (0.45f + 0.07f * j) + 0.5f * (0.8f + 0.12f * j) * s * 2.6f * peak);
            return best;
        }

        static Vector3 Ground(ZGame g, float x, float z) => new Vector3(x, g.GroundY(x, z), z);

        static void Circle(ZGame g, Vector3 c, float radius)
        {
            var sh = Shader.Find("Sprites/Default"); if (!sh) return;
            var go = new GameObject("b31 real radius (staging only)"); marks.Add(go);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = new Material(sh); lr.useWorldSpace = true; lr.loop = true; lr.widthMultiplier = 0.08f;
            lr.startColor = lr.endColor = new Color(1f, 1f, 1f, 0.9f); lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            const int N = 72; lr.positionCount = N;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N; var p = Ground(g, c.x + Mathf.Cos(a) * radius, c.z + Mathf.Sin(a) * radius);
                lr.SetPosition(i, p + Vector3.up * 0.1f);
            }
        }

        static IEnumerator Shot(string path, List<string> written)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (System.Exception) { }
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 4f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return 1;
            if (File.Exists(path)) written.Add(path.Replace('\\', '/'));
        }

        static IEnumerator Stage(ZGame g, RtsCamera rts)
        {
            var W = g.World; var sb = new StringBuilder(); var written = new List<string>();
            int bx, by; float far;
            if (TileX >= 0 && TileY >= 0) { bx = TileX - 12; by = TileY - 11; far = -1f; }
            else if (!FindPlace(W, out bx, out by, out far)) { Report = Tag + " NOT RUN: no open ground of 24 x 16 tiles found on this level; give a tile by hand (OzV11GrenadeStage.TileX / TileY)"; Debug.LogError(Report); yield break; }
            int rulePx = W.ThrownGrenadeRadius; float night = NightGrade.NightK; string when = night > 0.5f ? "night" : "day";
            var now = OzGrenadeBlast.For(rulePx);
            var rows = new (string name, LegacyMatch.BlastSpec spec, int realPx)[]
            {
                ("a grenade as it was drawn until now", LegacyMatch.Grenade, 30),
                ("a grenade as it is drawn now", now, rulePx),
                ("a Tough's rocket", LegacyMatch.Bazooka, 40),
                ("a light tank's shell", LegacyMatch.LightShell, 40),
            };
            sb.AppendLine($"Grenade blasts on screen: level {g.level}, Game view {Screen.width}x{Screen.height}, game speed {g.gameSpeed:0.##}, time scale {Time.timeScale:0.##}, time of day {NightGrade.TimeOfDay:0.000} (night value {night:0.00}: {when}).");
            sb.AppendLine($"The rule DV104 is {(ZWorld.GrenadeBoostDeviation ? "ON" : "OFF")}: a thrown grenade takes {W.ThrownGrenadeDamage} HP within {rulePx} px ({rulePx / 8f:0.00} m).");
            sb.AppendLine($"Place: tiles {bx},{by} to {bx + 23},{by + 15}" + (far >= 0f ? $", found by the helper; the nearest unit is {far / 16f:0.0} tiles from its middle." : ", given by hand."));
            sb.AppendLine("From west to east, 9 m apart (strength, glow radius, how far the fire reaches at its largest, the blast's real radius, fire / real):");
            foreach (var r in rows)
                sb.AppendLine($"  {r.name}: strength {r.spec.s:0.00}, glow radius {r.spec.legRadius:0.0} m, fire to {FireEdge(r.spec.s):0.00} m, real radius {r.realPx} px = {r.realPx / 8f:0.00} m, {100f * FireEdge(r.spec.s) / (r.realPx / 8f):0} %");
            focus0 = rts.focus; height0 = rts.height; camKept = true;
            var ears = Object.FindFirstObjectByType<AudioListener>();
            for (int row = 0; row < 2; row++)
            {
                bool circles = row == 1;
                var mid = ZGame.PxToWorld((bx + 12) * 16, (by + 11) * 16); mid.z += row * RowApart; mid.y = 0f;   // row 2 lies 7 tiles further north, still inside the box
                rts.focus = mid; rts.SetHeightImmediate(CamHeight);
                yield return 0.6f;                                                                          // the camera has settled
                var at = new Vector3[rows.Length];
                for (int i = 0; i < rows.Length; i++) at[i] = Ground(g, mid.x + (i - 1.5f) * Apart, mid.z);
                if (circles) for (int i = 0; i < rows.Length; i++) Circle(g, at[i], rows[i].realPx / 8f);
                if (circles) yield return 2;
                float t0 = Time.unscaledTime, real0 = Time.realtimeSinceStartup; int drawn0 = OzGrenadeBlast.Drawn;
                for (int i = 0; i < rows.Length; i++)
                {
                    int id = 31000 + row * 10 + i;
                    if (i == 1)
                    {   // the game's own path for a landing grenade (CombatView MissileExploded -> LegacyMatch.MissileBlast): look and sound
                        int px = Mathf.RoundToInt(at[i].x * 8f), py = Mathf.RoundToInt(-at[i].z * 8f);
                        LegacyMatch.MissileBlast(new ZEvent { type = ZEventType.MissileExploded, objId = -1, otherId = -1, x = px, y = py, radius = rulePx, flag = true, value = W.ThrownGrenadeDamage }, null, null, at[i], FxManager.I, CombatAudio.I);
                    }
                    else LegacyMatch.DoBlast(at[i] + Vector3.up * 0.2f, rows[i].spec, id, FxManager.I);
                }
                string stem = Path.Combine(dir, "for_owner", $"b31_{when}_{(circles ? "2_real_radius_marked" : "1_clean")}");
                foreach (var (label, wait) in new[] { ("a_fire", 0.22f), ("b_ring", 0.50f), ("c_marks", 2.60f) })
                {
                    while (Time.realtimeSinceStartup - real0 < wait) { rts.focus = mid; yield return 1; }
                    float taken = Time.realtimeSinceStartup - real0;
                    yield return Shot(stem + "_" + label + ".png", written);
                    sb.AppendLine($"  row {row + 1} ({(circles ? "the real radius marked" : "clean")}), {label}: asked for {taken:0.00} s after the blasts");
                }
                if (row == 0)
                {
                    var heard = OzSoundLog.Since(t0).FindAll(e => e.key != null && e.key.Contains("explosion"));
                    float dist = ears ? Vector3.Distance(ears.transform.position, at[1]) : -1f;
                    sb.AppendLine($"Row 1: the grenade's blast went through the game's own path {OzGrenadeBlast.Drawn - drawn0} time(s): radius {OzGrenadeBlast.LastRadiusPx} px, strength {OzGrenadeBlast.LastStrength:0.00}, glow radius {OzGrenadeBlast.LastGlowRadius:0.0} m.");
                    sb.AppendLine($"Sound log of that blast ({heard.Count} line(s) with the key 'explosion'; the listener is {dist:0} m from it; time, source, key, clip, outcome, volume, distance):");
                    foreach (var line in OzSoundLog.Format(heard).Split('\n')) if (line.Trim().Length > 0) sb.Append("    ").AppendLine(line.TrimEnd());
                }
                foreach (var m in marks) if (m) Object.Destroy(m);
                marks.Clear();
                yield return 1.0f;
            }
            sb.AppendLine("Pictures:");
            foreach (var p in written) sb.AppendLine("  " + p);
            bool ok = written.Count == 6;
            Report = $"{Tag} {(ok ? "DONE" : "INCOMPLETE")} {written.Count}/6 pictures ({when}, level {g.level}, {Screen.width}x{Screen.height}); text {Path.Combine(dir, "on_screen_" + when + ".txt").Replace('\\', '/')}";
            sb.AppendLine(Report);
            try { File.WriteAllText(Path.Combine(dir, "on_screen_" + when + ".txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning(Tag + " could not write the text: " + ex.Message); }
            if (ok) Debug.Log(Report); else Debug.LogError(Report);
        }
    }
}
