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
    ///  CPU bots cleared, day clock stopped. The player's (red) fort: idle check (no target: the head stays flat), an unkillable, unarmed,
    ///  immobile blue light tank beside the fort (east or west, inside the DV7 envelope: the gun turns side-on to the RTS camera).
    ///  Shot 1 with FortGunView.HeadTiltEnabled = false (BEFORE: key frames), shot 2 with the tilt (AFTER: a 60 fps strip,
    ///  Time.captureFramerate 60, crops around the gun + full key frames + per-frame tilt / bore elevation CSV), shot 3 at the default
    ///  RTS zoom, shot 4 by night. Then the blue fort fires at an unkillable red tank (AFTER key frames), and the production badge frames
    ///  over the own and the enemy fort (default zoom). Reload waits run the game at 4x (the fort mortar reloads 30 s).
    /// Heat: stops at 86 C held 30 s or 90 C at once (GpuGuard).
    /// </summary>
    public class FortGunTiltProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        string job, dir; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> verdicts = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_fgtilt_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_fgtilt_auto"); PlayerPrefs.Save();
            var go = new GameObject("FortGunTiltProof"); DontDestroyOnLoad(go);
            go.AddComponent<FortGunTiltProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[fgtilt] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (c.StartsWith("[fgtilt]")) return;
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

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; Camera ucam; float h0;
        static void GameViewSize(int idx)
        {
            var gvt = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var p = gvt?.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return;
            foreach (var w in Resources.FindObjectsOfTypeAll(gvt)) p.SetValue(w, idx);
        }

        IEnumerator SkipBriefing()
        {
            float w0 = T; bool aborted = false; var pf = typeof(ZodBriefing).GetField("perf", BindingFlags.NonPublic | BindingFlags.Instance);
            double sim0 = W.Time;
            Application.targetFrameRate = WaitFps;
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
            Application.targetFrameRate = 60;
            Mark($"briefing over after {T - w0:0.0} s");
        }
        const int WaitFps = 24;

        // ---------------------------------------------------------------- helpers
        (int x, int y) Spot(int px, int py)
        {
            int tx = px >> 4, ty = py >> 4;
            for (int r = 0; r <= 4; r++)
                for (int j = ty - r; j <= ty + r; j++)
                    for (int i = tx - r; i <= tx + r; i++)
                    {
                        if (Mathf.Max(Mathf.Abs(i - tx), Mathf.Abs(j - ty)) != r || !W.Path.TilePassable(i, j, false) || !W.Path.TilePassable(i, j, true)) continue;
                        var c = W.Map.ClassAt(i, j); if (c == ZClass.Water || c == ZClass.DeepWater) continue;
                        return (i * 16 + 8, j * 16 + 8);
                    }
            return (-1, -1);
        }
        readonly List<ZObject> staged = new List<ZObject>();
        ZObject Target(ZObject fort, Team team)
        {
            var hu = W.S.Unit("howitzer"); int R = hu != null ? hu.attack_radius : 200;
            foreach (int side in new[] { -1, 1 })   // west first: the barrel points left, away from the HUD panels (L17 run 1: east, under the panel)
            {
                int x = side > 0 ? fort.LocX + fort.WidthPix + 104 : fort.LocX - 104, y = fort.CenterY;
                var (sx, sy) = Spot(x, y);
                if (sx < 0) continue;
                double d = Math.Sqrt((double)(sx - fort.CenterX) * (sx - fort.CenterX) + (double)(sy - fort.CenterY) * (sy - fort.CenterY));
                if (d > R - 10 || !fort.FortGunCanReach(sx, sy)) { Mark($"  side {side}: ({sx},{sy}) d {d:0} px not reachable"); continue; }
                var o = W.CreateObject("vehicle", "light", sx - 16, sy - 16, team); g.SpawnUnitView(o);
                o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); o.Damage = 0; o.AttackRadius = 0; o.MoveSpeed = 0; o.RealMoveSpeed = 0;
                staged.Add(o); lastSide = side;
                Mark($"target {o.Type} #{o.Id} ({team}) at px ({sx},{sy}), {d:0} px from the {fort.Owner} fort centre (side {(side > 0 ? "east" : "west")}), fort {fort.Type} {fort.WidthPix}x{fort.HeightPix} px");
                return o;
            }
            return null;
        }
        int lastSide = -1;
        /// <summary>Close frames: the gun left of the HUD panels, the barrel in the clear (west barrel: gun ~45 % across, east: ~25 %).</summary>
        Vector3 Shift(Vector3 at, int side) => at + Vector3.right * (side > 0 ? 4.5f : 1.2f);
        void Kill(ZObject o) { if (o != null && !o.Destroyed && W.Get(o.Id) == o) { o.SetHealth(0); W.UpdateObjectHealth(o); } }
        void Focus(Vector3 at, float h) { cam.focus = new Vector3(at.x, 0f, at.z) + Flat(cam.transform.forward).normalized * (at.y * 0.9f); cam.SetHeightImmediate(h); }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        float Left(ZObject fort) => (float)(fort.FortGunReadyAt - W.Time) / Mathf.Max(0.05f, g.gameSpeed);
        IEnumerator TimeOfDay(float tod) { dn.timeOfDay = tod; dn.Apply(tod); yield return Wait(1.2f); }

        /// <summary>Waits (4x while far) until the next shot is `lead` real seconds away at 1x.</summary>
        IEnumerator UntilShot(ZObject fort, ZObject tgt, float lead)
        {
            float w0 = T;
            while (!done && T - w0 < 40f)
            {
                if (fort.FortGunTarget != tgt) { g.gameSpeed = 1f; Application.targetFrameRate = 60; yield return null; continue; }   // the lay (1.4 s) is always at 1x
                double left = fort.FortGunReadyAt - W.Time;
                if (left <= lead) break;
                g.gameSpeed = left > lead + 3.0 ? 4f : 1f;
                Application.targetFrameRate = left > lead + 3.0 ? WaitFps : 60;                     // heat: the 4x reload wait draws at 24 fps
                lastMark = T; yield return null;
            }
            g.gameSpeed = 1f; Application.targetFrameRate = 60;
        }

        /// <summary>Captures one shot: frames at 60 fps (Time.captureFramerate) from `pre` s before to `post` s after it. Full-size PNGs
        /// at the key offsets (s, relative to the shot), crops around the gun as JPG when `strip`; per-frame tilt and bore elevation.</summary>
        IEnumerator Shoot(ZObject fort, FortGunView fv, string tag, float pre, float post, float[] keys, bool strip, StringBuilder csv)
        {
            int shots0 = fv.Shots; float maxTilt = 0f, boreAtShot = float.NaN, tiltAtShot = float.NaN, tiltIdle = float.NaN;
            Time.captureFramerate = 60;
            int f = 0, shotF = -1; var keyDone = new HashSet<int>();
            string sdir = Path.Combine(dir, "strip_" + tag); if (strip) Directory.CreateDirectory(sdir);
            int predicted = Mathf.RoundToInt(Left(fort) * 60f);
            var yawT = FindDeep(fv.transform, "FortGunYaw"); var muzT = FindDeep(fv.transform, "FortGunMuzzle");
            while (!done && f < 60 * (pre + post + 2f))
            {
                yield return new WaitForEndOfFrame();
                if (shotF < 0 && fv.Shots != shots0) { shotF = f; boreAtShot = fv.BoreElevationNow; tiltAtShot = fv.HeadTiltNow; }
                int rel = shotF >= 0 ? f - shotF : f - predicted;
                if (f == 0) tiltIdle = fv.HeadTiltNow;
                maxTilt = Mathf.Max(maxTilt, fv.HeadTiltNow);
                csv?.AppendLine($"{tag},{f},{rel},{W.Time:0.000},{fv.HeadTiltNow:0.00},{fv.BoreElevationNow:0.00},{fv.Elevation:0.00},{(shotF >= 0 ? 1 : 0)}");
                bool keyHit = false; string keyName = null;
                foreach (var k in keys)
                {
                    int kf = Mathf.RoundToInt(k * 60f);
                    if (rel == kf && !keyDone.Contains(kf) && (shotF >= 0 || kf < 0)) { keyDone.Add(kf); keyHit = true; keyName = $"{tag}_{(k < 0 ? "m" : "p")}{Mathf.Abs(k):0.00}s"; }
                }
                if (strip || keyHit)
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (keyHit) { File.WriteAllBytes(Path.Combine(dir, $"{g.level}_{keyName}.png"), tex.EncodeToPNG()); Mark($"frame {g.level}_{keyName}.png {tex.width}x{tex.height} tilt {fv.HeadTiltNow:0.0} bore {fv.BoreElevationNow:0.0} cam {cam.height:0} m"); }
                    if (strip && rel >= -Mathf.RoundToInt(pre * 60f))
                    {
                        var sp = yawT && muzT ? (ucam.WorldToScreenPoint(yawT.position) + ucam.WorldToScreenPoint(muzT.position)) * 0.5f
                                              : ucam.WorldToScreenPoint(fv.transform.position + Vector3.up * 8f);
                        int cw = Mathf.Min(1400, tex.width), ch = Mathf.Min(1000, tex.height);
                        int x0 = Mathf.Clamp(Mathf.RoundToInt(sp.x) - cw / 2, 0, tex.width - cw), y0 = Mathf.Clamp(Mathf.RoundToInt(sp.y) - ch / 2, 0, tex.height - ch);
                        var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false);
                        crop.SetPixels(tex.GetPixels(x0, y0, cw, ch)); crop.Apply();
                        File.WriteAllBytes(Path.Combine(sdir, $"f{f:000}_rel{rel:+000;-000}.jpg"), crop.EncodeToJPG(88));
                        Destroy(crop);
                    }
                    Destroy(tex);
                }
                if (shotF >= 0 && f - shotF >= Mathf.RoundToInt(post * 60f)) break;
                f++;
            }
            Time.captureFramerate = 0;
            Mark($"{tag}: shot at frame {shotF} (predicted {predicted}), tilt at the start {tiltIdle:0.00} deg, at the shot {tiltAtShot:0.00}, max {maxTilt:0.00}; bore elevation at the shot {boreAtShot:0.00} deg (lob {fv.mortarElevation:0}), frames {f}");
            lastShot = (shotF >= 0, tiltIdle, tiltAtShot, maxTilt, boreAtShot);
        }
        (bool fired, float idle, float atShot, float max, float bore) lastShot;

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            lastMark = T;
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) yield return null;
            if (!ZGame.I || ZGame.I.World == null) { Finish("the level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>(); ucam = cam.GetComponent<Camera>(); if (!ucam) ucam = Camera.main;
            dir = Path.Combine(Root, "work/records/milestones/task_fort_gun_tilt", g.level); Directory.CreateDirectory(dir);
            StartCoroutine(Heat());
            h0 = cam.height;
            Mark($"fort gun tilt Play proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, screen {Screen.width}x{Screen.height}");
            Mark($"level up after {T - w0:0.0} s, level {g.level}, default RTS height {h0:0.0} m, pitch {cam.pitch:0}; DV7 {ZWorld.FortGunDeviation}, DV25 mortar {ZWorld.FortMortarDeviation}");
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return SkipBriefing();
            if (done) yield break;
            bool run0 = dn.running; dn.running = false;
            yield return TimeOfDay(0.08f);
            var red = W.Objects.Find(o => o.IsFort && o.Owner == Team.Red && !o.Destroyed);
            var blue = W.Objects.Find(o => o.IsFort && o.Owner == Team.Blue && !o.Destroyed);
            FortGunView rv = null, bv = null;
            foreach (var v in FindObjectsByType<FortGunView>(FindObjectsSortMode.None)) { if (v.Fort == red) rv = v; if (v.Fort == blue) bv = v; }
            Mark($"CPU bots cleared ({bots}); forts red #{red?.Id} view {(rv ? "bound" : "none")} head {(rv && rv.HasHead)}, blue #{blue?.Id} view {(bv ? "bound" : "none")} head {(bv && bv.HasHead)}");
            if (!rv || !bv || !rv.HasHead || !bv.HasHead) { Verdict("bind", false, "a fort gun view or its split head is missing"); Finish(null); yield break; }
            Verdict("bind", true, "both fort guns bound with a split head (FortGunHead) and 2 rams");
            var gunR = rv.transform.position; var gunB = bv.transform.position;
            var yawR = FindDeep(rv.transform, "FortGunYaw"); var yawB = FindDeep(bv.transform, "FortGunYaw");
            Vector3 focR = yawR ? yawR.position : gunR, focB = yawB ? yawB.position : gunB;
            if (job.EndsWith("_badge")) { yield return Badges(red, blue, focR, focB, true); dn.running = run0; Finish(null); yield break; }   // badge-only run

            // 1. idle: no target, the head sits flat
            Focus(focR, 24f);
            float idleMax = 0f; float i0 = T; while (T - i0 < 2.5f && !done) { idleMax = Mathf.Max(idleMax, rv.HeadTiltNow, bv.HeadTiltNow); yield return null; }
            var tex0 = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, $"{g.level}_idle_day_24.png"), tex0.EncodeToPNG()); Destroy(tex0);
            Verdict("idle_flat", idleMax < 0.01f, $"no target for 2.5 s: head tilt max {idleMax:0.000} deg (red + blue)");

            // 2. BEFORE: shot 1 with the head held flat
            var csv = new StringBuilder("tag,frame,rel_to_shot,sim_time,head_tilt_deg,bore_elev_deg,elev_field_deg,after_shot\n");
            var tgtR = Target(red, Team.Blue);
            if (tgtR == null) { Verdict("target_red", false, "no reachable tile east or west of the red fort"); Finish(null); yield break; }
            int sideR = lastSide;
            Focus(Shift(focR, lastSide), 24f);
            FortGunView.HeadTiltEnabled = false;
            yield return UntilShot(red, tgtR, 1.3f);
            yield return Shoot(red, rv, "before_day_24", 1.3f, 1.6f, new[] { -0.4f, 0.15f, 0.6f }, false, csv);
            var before = lastShot;
            FortGunView.HeadTiltEnabled = true;

            // 3. AFTER: shot 2, the 60 fps strip at 24 m
            yield return UntilShot(red, tgtR, 1.4f);
            yield return Shoot(red, rv, "after_day_24", 1.3f, 2.6f, new[] { -1.0f, -0.4f, 0.15f, 0.6f, 1.2f, 1.8f, 2.5f }, true, csv);
            var after = lastShot;
            Verdict("before_flat", before.fired && before.max < 0.01f, $"shot 1 (tilt off): fired {before.fired}, head tilt max {before.max:0.00} deg, bore {before.bore:0.00} deg");
            Verdict("after_tilts", after.fired && after.atShot > rv.headTilt * 0.9f && after.idle < 0.5f, $"shot 2: head {after.idle:0.00} deg 1.3 s before, {after.atShot:0.00} deg at the shot, max {after.max:0.00} (set {rv.headTilt:0})");
            Verdict("lob_kept", after.fired && before.fired && Mathf.Abs(after.bore - before.bore) < 1.0f, $"bore elevation at the shot: before {before.bore:0.00}, after {after.bore:0.00} deg (the barrel's own elevation in the head shrinks by the tilt)");
            float settle0 = T; float settleTilt = 99f; while (T - settle0 < 3.5f && !done) { settleTilt = rv.HeadTiltNow; yield return null; }
            Verdict("eases_back", settleTilt < 0.2f, $"~6 s after shot 2 (reloading, target still in range): head tilt {settleTilt:0.00} deg");

            // 4. shot 3 at the default RTS zoom
            Focus(focR, h0);
            yield return UntilShot(red, tgtR, 1.0f);
            yield return Shoot(red, rv, $"after_day_{h0:0}", 1.0f, 1.6f, new[] { -0.4f, 0.2f, 0.8f }, false, csv);

            // 5. shot 4 by night, 24 m
            yield return TimeOfDay(0.62f);
            Focus(Shift(focR, sideR), 24f);
            yield return UntilShot(red, tgtR, 1.0f);
            yield return Shoot(red, rv, "after_night_24", 1.0f, 1.6f, new[] { -0.4f, 0.2f, 0.8f }, false, csv);
            var night = lastShot;
            Verdict("night_tilts", night.fired && night.atShot > rv.headTilt * 0.9f, $"night shot: head {night.atShot:0.00} deg at the shot");
            Kill(tgtR);

            // 6. the blue fort (the other team), day
            yield return TimeOfDay(0.08f);
            var tgtB = Target(blue, Team.Red);
            if (tgtB == null) Verdict("target_blue", false, "no reachable tile east or west of the blue fort");
            else
            {
                Focus(Shift(focB, lastSide), 24f);
                yield return UntilShot(blue, tgtB, 1.0f);
                yield return Shoot(blue, bv, "blue_after_day_24", 1.0f, 2.2f, new[] { -0.4f, 0.15f, 0.6f, 2.0f }, false, csv);
                var bl = lastShot;
                Verdict("blue_tilts", bl.fired && bl.atShot > bv.headTilt * 0.9f, $"blue fort shot: head {bl.atShot:0.00} deg at the shot, bore {bl.bore:0.00} deg");
                Focus(focB, h0);
                yield return UntilShot(blue, tgtB, 1.0f);
                yield return Shoot(blue, bv, $"blue_after_day_{h0:0}", 1.0f, 1.2f, new[] { 0.2f }, false, csv);
                Kill(tgtB);
            }
            File.WriteAllText(Path.Combine(dir, "tilt_strip.csv"), csv.ToString());

            // 7. production badge frames over the own and the enemy fort (default zoom, day)
            yield return Badges(red, blue, focR, focB, true);
            Mark($"tilt cycles red {rv.HeadTiltCycles}, blue {bv.HeadTiltCycles}; max red {rv.HeadTiltMax:0.00}, blue {bv.HeadTiltMax:0.00}");
            dn.running = run0;
            Finish(null);
        }

        void Snap(string file, string what)
        {
            var tb = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, file), tb.EncodeToPNG()); Destroy(tb);
            Mark($"frame {file}; {what}");
        }

        /// <summary>production_badge_v2 over the forts: the own (red) and the enemy (blue) fort at the default zoom; with `states` also the
        /// own fort at 24 m and every forced badge state (OzBadgeState.Force, the view-only proof override of production_badge_v2).</summary>
        IEnumerator Badges(ZObject red, ZObject blue, Vector3 focR, Vector3 focB, bool states)
        {
            yield return Wait(1.0f);
            Focus(focR, h0); yield return Wait(0.8f);
            Snap($"{g.level}_badge_ownfort_{h0:0}.png", $"red fort building {red.BuildUnitType ?? "-"}, badge rects {OpenZUnitOverlay.BadgeRects.Count}");
            Focus(focB, h0); yield return Wait(0.8f);
            Snap($"{g.level}_badge_enemyfort_{h0:0}.png", $"blue fort building {blue.BuildUnitType ?? "-"}, badge rects {OpenZUnitOverlay.BadgeRects.Count}");
            if (!states) yield break;
            Focus(focR, 24f); yield return Wait(0.8f);
            Snap($"{g.level}_badge_ownfort_24.png", "own fort close");
            Focus(focR, h0); yield return Wait(0.6f);
            foreach (var f in new[] { OzBadgeForce.Ready, OzBadgeForce.Paused, OzBadgeForce.Place, OzBadgeForce.PlaceIdle, OzBadgeForce.Idle })
            {
                OzBadgeState.Force = f; yield return Wait(0.7f);
                Snap($"{g.level}_badge_ownfort_{h0:0}_{f}.png", $"forced state {f}, badge rects {OpenZUnitOverlay.BadgeRects.Count}");
            }
            OzBadgeState.Force = OzBadgeForce.None;
            Focus(focB, h0); OzBadgeState.Force = OzBadgeForce.Ready; yield return Wait(0.7f);
            Snap($"{g.level}_badge_enemyfort_{h0:0}_Ready.png", "enemy fort, forced READY (the enemy shows no READY: fade)");
            OzBadgeState.Force = OzBadgeForce.None;
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                Time.captureFramerate = 0; FortGunView.HeadTiltEnabled = true; OzBadgeState.Force = OzBadgeForce.None; Application.targetFrameRate = 60;
                foreach (var o in staged) Kill(o);
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                if (dir != null) File.WriteAllText(Path.Combine(dir, job + "_proof_log.txt"), log.ToString());
                GameViewSize(9);
            }
            catch (Exception e) { Debug.LogError("[fgtilt] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
