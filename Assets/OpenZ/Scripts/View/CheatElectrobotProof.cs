#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// events, like OzCheatSpawner.TestRoutine): P opens the cheat overlay, the RED and the BLUE "ELECTRO x1" tiles are dragged onto the
    /// L02 map middle, P closes it; the pair (made unkillable so the fight lasts) is ordered at each other for 25 s. Verdicts: overlay +
    /// both tiles, both spawns through the cheat, the pair zaps each other with no stun (D4: Electrobots are immune), then a red and a blue
    /// Tough squad dragged in the same way get stunned by the enemy Electrobot (StunCount > 0), wpn_electro_fire in OzSoundLog, 0 console errors.
    /// Frames at the Game view size (2560x1600): overlay with the ghost mid-drag, overlay + the spawned pair, the pair fighting at the RTS
    /// </summary>
    public class CheatElectrobotProof : MonoBehaviour
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/task_cheat_electrobot"));
        string job; bool done; int errors; float lastMark, hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder(); readonly List<string> verdicts = new List<string>();
        readonly List<int> spawnedIds = new List<int>();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_cheateb_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_cheateb_auto"); PlayerPrefs.Save();
            var go = new GameObject("CheatElectrobotProof"); DontDestroyOnLoad(go);
            go.AddComponent<CheatElectrobotProof>().job = job;
        }

        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[cheateb] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }
        static IEnumerator F(int n) { for (int i = 0; i < n; i++) yield return null; }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c}"); }
            if (c.StartsWith("[OzCheat] spawned"))
            {
                log.AppendLine($"[{T,7:0.00}] console: {c}");
                int k = c.IndexOf(" id "); if (k > 0) { var rest = c.Substring(k + 4); int sp = rest.IndexOf(' '); int id; if (int.TryParse(sp > 0 ? rest.Substring(0, sp) : rest, out id)) spawnedIds.Add(id); }
            }
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
                if (T - lastMark > 120f) { Finish("watchdog: no progress for 120 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ---------------------------------------------------------------- real input
        static void Mouse(Vector2 p, bool left)
        {
            var st = new MouseState { position = p }; if (left) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, st);
        }
        static IEnumerator KeyP()
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.P)); yield return F(2);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return F(2);
        }
        void Shot(string name)
        {
            try { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png")); Mark($"frame {name}.png ({Screen.width}x{Screen.height}, camera height {(cam ? cam.height : -1f):0.0} m)"); }
            catch (Exception e) { Mark("shot failed " + e.Message); }
        }

        ZGame g; ZWorld W; RtsCamera cam; OpenZHud hud;
        RectTransform Tile(string column)
        {
            var col = hud.Root.Find(column); if (!col) return null;
            var c = col.Find("Cell_" + ZElectro.Type); return c as RectTransform;
        }
        static string LabelOf(RectTransform cell)
        {
            if (!cell) return "(none)"; var n = cell.Find("Name"); if (!n) return "(no label)";
            var tmp = n.GetComponentInChildren<TMPro.TMP_Text>(); if (tmp) return tmp.text;
            var leg = n.GetComponentInChildren<UnityEngine.UI.Text>(); return leg ? leg.text : "(no text)";
        }
        static Vector2 Centre(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        Vector2 ScreenOf(int px, int py) { var w = Camera.main.WorldToScreenPoint(ZGame.PxToWorld(px, py)); return new Vector2(w.x, w.y); }

        IEnumerator Drag(RectTransform tile, Vector2 drop, string midShot)
        {
            Vector2 c = Centre(tile);
            Mouse(c, false); yield return F(2); Mouse(c, true); yield return F(2);
            for (int k = 1; k <= 8; k++)
            {
                Mouse(Vector2.Lerp(c, drop, k / 8f), true); yield return F(1);
                if (k == 5 && midShot != null) { Shot(midShot); yield return F(2); }
            }
            Mouse(drop, true); yield return F(2);
            Mouse(drop, false); yield return F(4);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir); lastMark = T;
            Mark($"cheat electrobot proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Heat());
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null || !OpenZHud.I || !OpenZHud.I.Root || !OzCheatSpawner.I) && T - w0 < 100f) { lastMark = T; yield return null; }
            if (!ZGame.I || ZGame.I.World == null || !OzCheatSpawner.I) { Finish($"level / cheat never came up in 100 s (cheat {(bool)OzCheatSpawner.I}, dev {OzCheatSpawner.DevEnabled})"); yield break; }
            g = ZGame.I; W = g.World; hud = OpenZHud.I; cam = FindFirstObjectByType<RtsCamera>();
            Mark($"level up after {T - w0:0.0} s; cheat installed {(bool)OzCheatSpawner.I}, dev {OzCheatSpawner.DevEnabled}");
            int bots = W.Bots.Count; W.Bots.Clear();

            // the map middle between the forts, on a clear robot tile
            ZObject rf = null, bf = null; foreach (var o in W.Objects) if (o.IsFort && !o.Destroyed) { if (o.Owner == Team.Red) rf = o; else if (o.Owner == Team.Blue) bf = o; }
            int mx = (rf.CenterX + bf.CenterX) / 2, my = (rf.CenterY + bf.CenterY) / 2;
            int ax, ay; OzCheatSpawner.FreeTile(W, mx >> 4, my >> 4, 1, true, out ax, out ay);
            int redPx = ax * 16 - 40, bluePx = ax * 16 + 56, rowPy = ay * 16 + 8;
            if (cam) { var p = ZGame.PxToWorld((redPx + bluePx) / 2 + 48, rowPy + 16); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(cam.height); }   // pair left of the unit panel
            Mark($"CPU bots cleared ({bots}); middle tile ({ax},{ay}); camera height {(cam ? cam.height : -1f):0.0} m");
            yield return Wait(3f);

            // 1. P shows the overlay with both ELECTRO tiles
            yield return KeyP();
            yield return F(3);
            var red = Tile("CheatRed"); var blue = Tile("CheatBlue");
            string rl = LabelOf(red), bl = LabelOf(blue);
            Verdict("C1 P overlay + tiles", OzCheatSpawner.Visible && red && blue && rl.StartsWith("ELECTRO") && bl.StartsWith("ELECTRO"),
                $"visible {OzCheatSpawner.Visible}, red tile '{rl}', blue tile '{bl}', robot tiles per column {(red ? red.parent.childCount : 0)} nodes");
            if (!red || !blue) { Finish("tiles missing"); yield break; }
            yield return Wait(0.5f);
            Shot($"{job}_1_overlay_open");

            // 2. drag red + blue onto the map (real mouse events)
            Vector2 rd = ScreenOf(redPx, rowPy), bd = ScreenOf(bluePx, rowPy);
            Mark($"drop points red {rd} blue {bd}; over UI red {hud.IsOverUi(rd)} blue {hud.IsOverUi(bd)}");
            int n0 = W.Objects.Count;
            yield return Drag(red, rd, $"{job}_2_overlay_dragging_red");
            yield return Wait(0.3f);
            yield return Drag(blue, bd, null);
            yield return Wait(0.6f);
            ZObject re = null, be = null;
            foreach (var id in spawnedIds) { var o = W.Get(id); if (o == null || o.Type != ZElectro.Type) continue; if (o.Owner == Team.Red) re = o; else if (o.Owner == Team.Blue) be = o; }
            bool views = re != null && be != null && g.Views.ContainsKey(re.Id) && g.Views.ContainsKey(be.Id);
            Verdict("C2 cheat spawns", re != null && be != null && views,
                $"objects +{W.Objects.Count - n0}; red {(re != null ? "#" + re.Id + " " + re.Type + " at tile (" + (re.CenterX >> 4) + "," + (re.CenterY >> 4) + "), minions " + re.Minions.Count : "none")}; blue {(be != null ? "#" + be.Id + " " + be.Type + " at tile (" + (be.CenterX >> 4) + "," + (be.CenterY >> 4) + "), minions " + be.Minions.Count : "none")}; views {views}");
            Shot($"{job}_3_overlay_with_pair");
            yield return Wait(0.4f);
            yield return KeyP();
            Mark($"P again: overlay visible {OzCheatSpawner.Visible}");
            if (re == null || be == null) { Finish("spawn failed"); yield break; }

            // 3. the fight
            foreach (var o in new[] { re, be }) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
            float f0 = Time.unscaledTime;
            bool o1 = W.SendWaypoints(Team.Red, re, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = be.Id, x = be.CenterX, y = be.CenterY } });
            bool o2 = W.SendWaypoints(Team.Blue, be, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = re.Id, x = re.CenterX, y = re.CenterY } });
            Mark($"attack orders red -> blue {o1}, blue -> red {o2} (both unkillable so the fight lasts 25 s)");
            bool boltShot = false, closeShot = false; float fightEnd = T + 14f; int fires = 0;
            while (T < fightEnd && !done)
            {
                int nf = 0; foreach (var e in OzSoundLog.Since(f0)) if (e.key == "wpn_electro_fire" && e.outcome != null && e.outcome.StartsWith("PLAY")) nf++;
                if (nf > fires) { fires = nf; if (!boltShot) { boltShot = true; yield return null; Shot($"{job}_4_bolt_rts"); } }
                if (!closeShot && T > fightEnd - 6f && cam)
                {
                    closeShot = true; float h0 = cam.height; cam.SetHeightImmediate(h0 * 0.55f);
                    yield return Wait(0.6f); Shot($"{job}_5_pair_close"); yield return Wait(0.3f); cam.SetHeightImmediate(h0);
                }
                yield return null;
            }
            Verdict("C3 pair: no stun between Electrobots (D4)", re.StunCount == 0 && be.StunCount == 0 && ZWorld.ElectrobotImmune && fires > 0,
                $"bolts {fires}, red StunCount {re.StunCount}, blue StunCount {be.StunCount}, ZWorld.ElectrobotImmune {ZWorld.ElectrobotImmune} (D4: an Electrobot is never stunned)");

            // 4. stun: each cheat Electrobot vs an enemy Tough squad, also dragged in from the overlay
            yield return KeyP(); yield return F(3);
            RectTransform rtT = null, btT = null;
            { var c = hud.Root.Find("CheatRed"); if (c) rtT = c.Find("Cell_tough") as RectTransform; c = hud.Root.Find("CheatBlue"); if (c) btT = c.Find("Cell_tough") as RectTransform; }
            if (!rtT || !btT) { Finish("tough tiles missing"); yield break; }
            spawnedIds.Clear();
            Vector2 rtd = ScreenOf(bluePx + 16, rowPy + 56), btd = ScreenOf(redPx - 16, rowPy + 56);
            yield return Drag(rtT, rtd, null); yield return Wait(0.3f);
            yield return Drag(btT, btd, null); yield return Wait(0.5f);
            ZObject rT = null, bT = null;
            foreach (var id in spawnedIds) { var o = W.Get(id); if (o == null || o.Type != "tough") continue; if (o.Owner == Team.Red) rT = o; else if (o.Owner == Team.Blue) bT = o; }
            Shot($"{job}_6_overlay_toughs_dropped");
            yield return Wait(0.3f); yield return KeyP();
            if (rT == null || bT == null) { Finish($"tough spawn failed (red {rT != null}, blue {bT != null})"); yield break; }
            var squadR = new List<ZObject> { rT }; squadR.AddRange(rT.Minions); var squadB = new List<ZObject> { bT }; squadB.AddRange(bT.Minions);
            foreach (var o in squadR) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
            foreach (var o in squadB) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
            bool o3 = W.SendWaypoints(Team.Red, re, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = bT.Id, x = bT.CenterX, y = bT.CenterY } });
            bool o4 = W.SendWaypoints(Team.Blue, be, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = rT.Id, x = rT.CenterX, y = rT.CenterY } });
            Mark($"cheat Toughs: red #{rT.Id} ({squadR.Count}) at tile ({rT.CenterX >> 4},{rT.CenterY >> 4}), blue #{bT.Id} ({squadB.Count}) at tile ({bT.CenterX >> 4},{bT.CenterY >> 4}); orders red Electrobot -> blue Tough {o3}, blue Electrobot -> red Tough {o4} (all unkillable)");
            System.Func<List<ZObject>, int> stuns = l => { int n = 0; foreach (var o in l) n += o.StunCount; return n; };
            bool stunFrame = false, stunClose = false; float end2 = T + 18f;
            while (T < end2 && !done)
            {
                if (!stunFrame && (stuns(squadR) > 0 || stuns(squadB) > 0)) { stunFrame = true; yield return Wait(0.4f); Shot($"{job}_7_tough_stunned_rts"); }
                if (stunFrame && !stunClose && stuns(squadR) > 0 && stuns(squadB) > 0 && cam)
                {
                    stunClose = true; float h0 = cam.height; cam.SetHeightImmediate(h0 * 0.55f);
                    yield return Wait(0.6f); Shot($"{job}_8_both_toughs_stunned_close"); yield return Wait(0.3f); cam.SetHeightImmediate(h0);
                    end2 = Mathf.Min(end2, T + 2f);
                }
                yield return null;
            }
            Verdict("C3b cheat Electrobots stun", stuns(squadB) > 0 && stuns(squadR) > 0,
                $"red Electrobot -> blue Tough squad StunCount {stuns(squadB)} (stunned now {bT.Stunned}), blue Electrobot -> red Tough squad StunCount {stuns(squadR)} (stunned now {rT.Stunned}), world ElectroStuns {W.ElectroStuns}, bolts {W.ElectroBolts}");
            var es = OzSoundLog.Since(f0);
            var plays = new SortedDictionary<string, int>();
            foreach (var e in es) if (e.outcome != null && e.outcome.StartsWith("PLAY") && e.key != null && e.key.Contains("electro")) plays[e.key] = plays.ContainsKey(e.key) ? plays[e.key] + 1 : 1;
            var pb = new StringBuilder(); foreach (var kv in plays) pb.Append($"{kv.Key} x{kv.Value}; ");
            File.WriteAllText(Path.Combine(Dir, job + "_ozsoundlog.txt"), OzSoundLog.Format(es));
            int zap = plays.ContainsKey("wpn_electro_fire") ? plays["wpn_electro_fire"] : 0;
            Verdict("C4 zap sound", zap > 0, $"OzSoundLog electro plays: {pb}");
            Finish(null);
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                Verdict("C5 console clean", errors == 0, $"errors/exceptions during the run {errors}");
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                File.WriteAllText(Path.Combine(Dir, job + "_proof_log.txt"), log.ToString());
            }
            catch (Exception e) { Debug.LogError("[cheateb] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
