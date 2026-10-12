using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// calls (spawns, orders, damage, sim missiles, production, zone awards) and saves full-look evidence frames (Game view,
    /// seen during the step and simple PASS/FAIL checks). Started by the editor menu "OpenZ/Gate Battery" through PlayerPrefs
    /// "oz_gate" = 1 (the menu queues L02, L17 and the main menu). Heat: the run holds a 20 fps cap (the frames are still the
    /// full look), still shots freeze the sim first, and GpuGuard's pauses simply pause the whole run.
    /// </summary>
    [DefaultExecutionOrder(44)]   // LateUpdate reads ZWorld.Events before CombatView (50) clears them
    public class GateBattery : MonoBehaviour
    {
        public const string Pref = "oz_gate";
        public static bool Done { get; private set; }
        public static bool Running { get; private set; }

        ZGame G; ZWorld W; RtsCamera rts; Camera cam; DayNight dn; PlayerInput input; OpenZHud hud;
        string level, outDir; float savedScale = -1f;
        readonly List<ZObject> spawned = new List<ZObject>();
        readonly List<RectInt> reserved = new List<RectInt>();
        readonly StringBuilder json = new StringBuilder();
        Step cur; int stepNo;
        readonly List<Step> steps = new List<Step>();

        class Step
        {
            public int n; public string name; public float t0, t1; public double sim0, sim1;
            public readonly List<string> frames = new List<string>(), events = new List<string>(), checks = new List<string>(), notes = new List<string>();
            public bool failed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Done = false;
            if (PlayerPrefs.GetInt(Pref, 0) != 1 || FindFirstObjectByType<GateBattery>()) return;
            var go = new GameObject("GateBattery"); DontDestroyOnLoad(go); go.AddComponent<GateBattery>();
        }

        /// <summary>Capture kit F7 + number: 0 = gate battery; &gt; 0 = that showcase scene (no screenshots, no log, units stay).</summary>
        public int showcase;
        public static readonly string[] ShowcaseNames = { "", "vehicles manoeuvre", "driver snipe + steal", "rocks + sandbags blasted",
            "flag capture", "blue fort falls", "bot vs bot 4x", "lone robot walks into the blue fort", "robot line-up close",
            "explosion grid (turret toss)", "building breaks apart", "weapon line-up firing" };

        void Start() { StartCoroutine(showcase > 0 ? ShowcaseRun() : Main()); }

        IEnumerator ShowcaseRun()
        {
            G = ZGame.I; if (!G || G.World == null) yield break;
            W = G.World; level = G.level; Running = true;
            rts = FindFirstObjectByType<RtsCamera>(); cam = rts ? rts.GetComponent<Camera>() : Camera.main; if (!cam) cam = Camera.main;
            dn = FindFirstObjectByType<DayNight>(); input = FindFirstObjectByType<PlayerInput>(); hud = FindFirstObjectByType<OpenZHud>();
            var redFort = Find(o => o.Kind == ZKind.Building && o.Owner == Team.Red && o.Type.StartsWith("fort"));
            var mid = new Vector2Int(W.Map.width / 2, W.Map.height / 2);
            var fortT = redFort != null ? T(redFort) : mid;
            var home = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(fortT.x, mid.x, 0.55f)), Mathf.RoundToInt(Mathf.Lerp(fortT.y, mid.y, 0.55f)));
            Debug.Log($"[OpenZ] showcase {showcase}: {(showcase < ShowcaseNames.Length ? ShowcaseNames[showcase] : "?")}");
            switch (showcase)
            {
                case 1: yield return Run("vehicles_drive", () => Vehicles(home)); break;
                case 2: yield return Run("driver_snipe", () => Snipe(home)); break;
                case 3: yield return Run("rock_sandbag_break", () => RocksSandbags(home)); break;
                case 4: yield return Run("flag_capture", () => FlagCapture(home)); break;
                case 5: yield return Run("fort_falls", FortFalls); break;
                case 6: yield return Run("bot_vs_bot", BotVsBot); break;
                case 7: yield return Run("walk_into_fort", WalkIntoFort); break;
                case 8: yield return Run("robot_types", () => RobotTypes(home)); break;
                case 9: yield return Run("explosion_grid", () => ExplosionGrid(home)); break;
                case 10: yield return Run("building_destruction", () => BuildingKill(home)); break;
                case 11: yield return Run("weapon_lineup", () => WeaponLineup(home)); break;
            }
            FreeCam(false); Running = false; Destroy(gameObject);
        }

        IEnumerator FortFalls()
        {
            var bf = Find(o => o.Kind == ZKind.Building && o.Owner == Team.Blue && o.Type.StartsWith("fort"));
            if (bf == null) yield break;
            var p = WPos(bf); Rts(p, 55f); yield return Settle(2f);
            Kill(bf);
            yield return new WaitForSeconds(8f);
        }

        IEnumerator BotVsBot()
        {
            W.Bots.RemoveAll(b => b.Team == Team.Red);
            W.Bots.Add(new ZBot(W, Team.Red, G.seed + 303) { Difficulty = ZBotDifficulty.Normal });
            G.gameSpeed = 4f; Rts(TileWorld(new Vector2Int(W.Map.width / 2, W.Map.height / 2)), 120f);
            yield return new WaitForSecondsRealtime(40f);
        }

        IEnumerator WalkIntoFort()
        {
            var bf = Find(o => o.Kind == ZKind.Building && o.Owner == Team.Blue && o.Type.StartsWith("fort"));
            if (bf == null) yield break;
            Vector2Int gate = T(bf); if (bf.GetCreationMovePoint(out int gx, out int gy)) gate = new Vector2Int(gx / 16, gy / 16);
            var start = Open(gate, 2, 2, 4);
            var g = Spawn("robot", "grunt", start.xMin, start.yMin, Team.Red, 1);
            Rts(WPos(bf), 40f);
            Order(g, bf, WpMode.EnterFort);
            float t = 0; while (t < 40f && !W.GameOver) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(3f);
        }

        void OnDestroy()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp && savedScale > 0) urp.renderScale = savedScale;                            // also when Play is stopped mid-run
        }

        // ------------------------------------------------------------------ main
        IEnumerator Main()
        {
            Running = true;
            yield return new WaitForSecondsRealtime(2f);
            Application.targetFrameRate = 8; QualitySettings.vSyncCount = 0;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) savedScale = urp.renderScale;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene.ToLower().Contains("menu")) { level = "MENU"; yield return MenuPass(); }
            else
            {
                float wait = 0; while ((!(G = ZGame.I) || G.World == null) && wait < 30f) { wait += Time.unscaledDeltaTime; yield return null; }
                if (!G || G.World == null) { Debug.LogError("[Gate] no ZGame/World"); Finish(); yield break; }
                W = G.World; level = G.level;
                yield return LevelPass();
            }
            Finish();
        }

        void Finish()
        {
            WriteLog();
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 1;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp && savedScale > 0) urp.renderScale = savedScale;
            Running = false; Done = true;
            Debug.Log($"[Gate] {level}: {steps.Count} steps, {CountFrames()} frames -> {outDir}");
        }

        int CountFrames() { int n = 0; foreach (var s in steps) n += s.frames.Count; return n; }

        void Setup()
        {
            outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/gate", level));
            Directory.CreateDirectory(outDir);
            var onlyPref = PlayerPrefs.GetString("oz_gate_only", "");
            if (onlyPref.Length > 0) { outDir = Path.Combine(outDir, "recheck_" + DateTime.Now.ToString("HHmm")); Directory.CreateDirectory(outDir); }   // time-stamped: cited evidence is never overwritten
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
        }

        // ------------------------------------------------------------------ level pass
        IEnumerator LevelPass()
        {
            Setup();
            rts = FindFirstObjectByType<RtsCamera>(); cam = rts ? rts.GetComponent<Camera>() : Camera.main; if (!cam) cam = Camera.main;
            dn = FindFirstObjectByType<DayNight>(); input = FindFirstObjectByType<PlayerInput>(); hud = FindFirstObjectByType<OpenZHud>();
            W.Bots.Clear();                                                          // a quiet stage: no CPU orders during the battery
            SetTime(0.08f);
            yield return new WaitForSeconds(3f);

            var redFort = Find(o => o.Kind == ZKind.Building && o.Owner == Team.Red && o.Type.StartsWith("fort"));
            var mid = new Vector2Int(W.Map.width / 2, W.Map.height / 2);
            var fortT = redFort != null ? T(redFort) : mid;
            // staged scenes sit between the red fort and the map centre: away from the fort guns, still in red land
            var home = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(fortT.x, mid.x, 0.55f)), Mathf.RoundToInt(Mathf.Lerp(fortT.y, mid.y, 0.55f)));

            yield return Run("day_overview", () => Overview(false));
            yield return Run("night_overview", () => Overview(true));
            yield return Run("robot_types", () => RobotTypes(home));
            yield return Run("weapon_lineup", () => WeaponLineup(home));
            yield return Run("explosion_grid", () => ExplosionGrid(home));
            yield return Run("vehicles_drive", () => Vehicles(home));
            yield return Run("boarding", () => Boarding(home));
            yield return Run("driver_snipe", () => Snipe(home));
            yield return Run("flag_capture", () => FlagCapture(home));
            yield return Run("factory_cycle", () => Factory(home));
            yield return Run("cannon_flow", () => CannonFlow(home));
            yield return Run("rock_sandbag_break", () => RocksSandbags(home));
            yield return Run("building_destruction", () => BuildingKill(home));
            yield return Run("water_wading", () => Wading(home));
            yield return Run("critters", Critters);
            yield return Run("grenade_crate", () => Grenades(home));
            yield return Run("tracks_dust", () => Tracks(home));
            yield return Run("night_lights", () => NightLights(home));
            yield return Run("bridge_repair", () => BridgeRepair(home));
            yield return Run("ui_hud_selection", UiSelection);
            yield return Run("ui_production_groups", () => UiProduction(redFort));
            yield return Run("ui_pause", UiPause);
            yield return Run("ui_victory_defeat", UiEnd);
        }

        // ------------------------------------------------------------------ step runner
        IEnumerator Run(string name, Func<IEnumerator> body)
        {
            // PlayerPrefs oz_gate_only = "water_wading,boarding": only these steps (re-checks after fixes, less heat)
            var only = PlayerPrefs.GetString("oz_gate_only", "");
            if (only.Length > 0 && Array.IndexOf(only.Split(','), name) < 0) { ++stepNo; yield break; }
            cur = new Step { n = ++stepNo, name = name, t0 = Time.unscaledTime, sim0 = W != null ? W.Time : 0 };
            steps.Add(cur);
            Debug.Log($"[Gate] {level} step {cur.n:00} {name}");
            IEnumerator e = null;
            try { e = body(); } catch (Exception ex) { Fail(ex); }
            while (e != null)
            {
                object y;
                try { if (!e.MoveNext()) break; y = e.Current; } catch (Exception ex) { Fail(ex); break; }
                yield return y;
            }
            cur.t1 = Time.unscaledTime; cur.sim1 = W != null ? W.Time : 0;
            FreeCam(false);
            reserved.Clear();
            if (G && showcase != 6) { G.gameSpeed = 1f; G.paused = false; }
            Time.timeScale = 1f;
            if (showcase == 0) WriteLog();
        }

        void Fail(Exception ex) { cur.failed = true; cur.notes.Add("EXCEPTION " + ex.GetType().Name + ": " + ex.Message); Debug.LogException(ex); }
        void Check(string what, bool ok) { cur.checks.Add((ok ? "PASS " : "FAIL ") + what); }
        void Note(string s) { cur.notes.Add(s); }

        void LateUpdate()
        {
            if (cur == null || W == null) return;
            foreach (var e in W.Events)
            {
                if (cur.events.Count >= 400) break;
                if (e.type == ZEventType.Hitscan && cur.events.Count > 200) continue;
                var a = Obj(e.objId); var b = Obj(e.otherId);
                cur.events.Add($"{{\"t\":{W.Time:F2},\"type\":\"{e.type}\",\"obj\":\"{Desc(a, e.objId)}\",\"other\":\"{Desc(b, e.otherId)}\",\"x\":{e.x},\"y\":{e.y},\"value\":{e.value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"flag\":{(e.flag ? "true" : "false")}}}");
            }
        }

        ZObject Obj(int id) { if (id < 0 || W == null) return null; foreach (var o in W.Objects) if (o.Id == id) return o; return null; }
        static string Desc(ZObject o, int id) => o == null ? (id < 0 ? "" : "#" + id) : $"{o.Owner} {o.Type} #{o.Id}";

        // ------------------------------------------------------------------ capture
        IEnumerator Shot(string name)
        {
            if (showcase > 0) yield break;                                                        // capture kit showcase: no screenshots
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string file = $"{cur.n:00}_{cur.name}" + (cur.frames.Count == 0 ? "" : "_" + (char)('a' + cur.frames.Count)) + (string.IsNullOrEmpty(name) ? "" : "_" + name) + ".png";
            try { File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG()); cur.frames.Add(file + $" ({tex.width}x{tex.height})"); }
            catch (Exception ex) { Fail(ex); }
            Destroy(tex);
        }

        // ------------------------------------------------------------------ camera, time, helpers
        void SetTime(float t) { if (!dn) return; dn.running = false; dn.timeOfDay = t; dn.Apply(t); }

        void Rts(Vector3 focus, float h)
        {
            FreeCam(false);
            if (!rts) return;
            rts.focus = new Vector3(focus.x, 0, focus.z); rts.height = h; rts.ZoomTo(h);
        }

        void FreeCam(bool on) { if (rts) rts.enabled = !on; }

        void Close(Vector3 target, float dist, float yawDeg, float pitchDeg)
        {
            FreeCam(true);
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0);
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
        }

        IEnumerator Settle(float s = 1.2f) { yield return new WaitForSeconds(s); }

        Vector3 WPos(ZObject o) => G.CenterWorld(o);
        Vector3 TileWorld(Vector2Int t) { var p = ZGame.PxToWorld(t.x * 16 + 8, t.y * 16 + 8); p.y = G.GroundY(p.x, p.z); return p; }
        static Vector2Int T(ZObject o) => new Vector2Int(o.CenterX / 16, o.CenterY / 16);

        ZObject Find(Func<ZObject, bool> f) { foreach (var o in W.Objects) if (!o.Destroyed && o.KillAt < 0 && f(o)) return o; return null; }
        ZObject Nearest(Vector2Int t, Func<ZObject, bool> f)
        {
            ZObject best = null; float bd = float.MaxValue;
            foreach (var o in W.Objects) { if (o.Destroyed || o.KillAt >= 0 || !f(o)) continue; float d = (T(o) - t).sqrMagnitude; if (d < bd) { bd = d; best = o; } }
            return best;
        }

        bool TileFree(int x, int y)
        {
            if (x < 1 || y < 1 || x >= W.Map.width - 1 || y >= W.Map.height - 1) return false;
            var c = W.Map.ClassAt(x, y); return c == ZClass.Ground || c == ZClass.Road;
        }

        bool RectFree(RectInt r)
        {
            for (int y = r.yMin; y < r.yMax; y++) for (int x = r.xMin; x < r.xMax; x++) if (!TileFree(x, y)) return false;
            var e = new RectInt(r.xMin - 1, r.yMin - 1, r.width + 2, r.height + 2);
            foreach (var q in reserved) if (q.Overlaps(e)) return false;
            foreach (var o in W.Objects)
            {
                if (o.Destroyed || o.KillAt >= 0) continue;
                if (o.Kind == ZKind.Building && o.Type.StartsWith("fort")) { var fr = new RectInt(o.LocX / 16 - 8, o.LocY / 16 - 8, (o.WidthPix + 15) / 16 + 16, (o.HeightPix + 15) / 16 + 16); if (fr.Overlaps(e)) return false; continue; }
                var or = new RectInt(o.LocX / 16, o.LocY / 16, Mathf.Max(1, (o.WidthPix + 15) / 16), Mathf.Max(1, (o.HeightPix + 15) / 16));
                if (or.Overlaps(e)) return false;
            }
            return true;
        }

        /// <summary>A free w x h tile rectangle (ground / road, no objects, not used by an earlier step) near a tile.</summary>
        RectInt Open(Vector2Int near, int w, int h, int minR = 3)
        {
            for (int r = minR; r < 90; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var rect = new RectInt(near.x + dx - w / 2, near.y + dy - h / 2, w, h);
                        if (RectFree(rect)) { reserved.Add(rect); return rect; }
                    }
            Note($"no free {w}x{h} area near {near}"); var fb = new RectInt(near.x, near.y, w, h); reserved.Add(fb); return fb;
        }

        ZObject Spawn(string cat, string type, int tx, int ty, Team team, int amount = -1)
        {
            ZObject o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team, amount) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Note($"spawn {cat}:{type} failed"); return null; }
            var v = G.SpawnUnitView(o); if (!v && cat != "building") Note($"no view prefab for {type}");
            foreach (var m in o.Minions) G.SpawnUnitView(m);
            spawned.Add(o); return o;
        }

        bool Move(ZObject o, Vector2Int tile, WpMode mode = WpMode.Move, int refId = -1)
        {
            if (o == null) return false;
            var wp = new List<ZWaypoint> { new ZWaypoint { mode = mode, refId = refId, x = tile.x * 16 + 8, y = tile.y * 16 + 8, playerGiven = true } };
            return W.SendWaypoints(o.Owner, o, wp);
        }

        bool Order(ZObject o, ZObject target, WpMode mode)
        {
            if (o == null || target == null) return false;
            var wp = new List<ZWaypoint> { new ZWaypoint { mode = mode, refId = target.Id, x = target.CenterX, y = target.CenterY, playerGiven = true } };
            return W.SendWaypoints(o.Owner, o, wp);
        }

        void Kill(ZObject o)
        {
            if (o == null || o.Destroyed) return;
            foreach (var m in new List<ZObject>(o.Minions)) if (!m.Destroyed) { m.DamageHealth(m.Health + 1); W.UpdateObjectHealth(m, -1); }
            o.DamageHealth(o.Health + 1); W.UpdateObjectHealth(o, -1);
        }

        void Blast(int px, int py, int damage, int radius, bool grenade = false)
        {
            W.AddMissile(new ZMissile { x = px, y = py, damage = damage, radius = radius, team = Team.Neutral, grenade = grenade, explodeTime = W.Time });
        }

        void Clear()
        {
            if (showcase > 0) { spawned.Clear(); return; }                                         // showcase: the staged units stay in the game
            foreach (var o in spawned)
            {
                foreach (var m in o.Minions) Remove(m);
                Remove(o);
            }
            spawned.Clear();
        }

        void Remove(ZObject o)
        {
            if (o == null) return;
            if (o.KillAt < 0) o.KillAt = W.Time;
            if (G.Views.TryGetValue(o.Id, out var v)) { G.Views.Remove(o.Id); if (v) Destroy(v.gameObject); }
        }

        int Count(ZEventType t) { int n = 0; foreach (var e in cur.events) if (e.Contains("\"type\":\"" + t + "\"")) n++; return n; }

        // ------------------------------------------------------------------ scenarios
        IEnumerator Overview(bool night)
        {
            SetTime(night ? 0.62f : 0.08f);
            var c = TileWorld(new Vector2Int(W.Map.width / 2, W.Map.height / 2));
            Rts(c, rts ? rts.maxHeight : 200f); yield return Settle(3f); yield return Shot("max");
            Rts(c, 70f); yield return Settle(2f); yield return Shot("default");
            SetTime(0.08f);
        }

        static readonly string[] RobotTypeList = { "grunt", "psycho", "tough", "sniper", "pyro", "laser" };

        IEnumerator RobotTypes(Vector2Int home)
        {
            var area = Open(home, 12, 6);
            var made = new List<(string type, ZObject red, ZObject blue)>();
            for (int i = 0; i < RobotTypeList.Length; i++)
            {
                var red = Spawn("robot", RobotTypeList[i], area.xMin + 1 + i * 2, area.yMin + 1, Team.Red, 1);
                var blue = Spawn("robot", RobotTypeList[i], area.xMin + 1 + i * 2, area.yMin + 4, Team.Blue, 1);
                made.Add((RobotTypeList[i], red, blue));
            }
            G.paused = true;                                                          // a still line-up: nobody shoots
            var centre = TileWorld(new Vector2Int(area.xMin + 6, area.yMin + 3));
            Rts(centre, 26f); yield return Settle(2f); yield return Shot("rts_zoom");
            foreach (var m in made)
            {
                if (m.red == null) continue;
                var a = WPos(m.red); var b = m.blue != null ? WPos(m.blue) : a; var mid2 = (a + b) * 0.5f + Vector3.up * 1.2f;
                Close(mid2, 10f, 270f, 16f); yield return Settle(0.8f); yield return Shot(m.type);
            }
            G.paused = false;
            Clear();
        }

        IEnumerator WeaponLineup(Vector2Int home)
        {
            var area = Open(home, 12, 7);
            for (int i = 0; i < RobotTypeList.Length; i++)
            {
                Spawn("robot", RobotTypeList[i], area.xMin + 1 + i * 2, area.yMin + 1, Team.Red, 2);
                Spawn("robot", RobotTypeList[i], area.xMin + 1 + i * 2, area.yMin + 6, Team.Blue, 2);
            }
            var centre = TileWorld(new Vector2Int(area.xMin + 6, area.yMin + 3));
            Rts(centre, 34f);
            yield return new WaitForSeconds(2.5f); yield return Shot("firing");
            yield return new WaitForSeconds(0.6f); yield return Shot("firing2");
            Rts(centre, 22f); yield return new WaitForSeconds(0.9f); yield return Shot("close");
            yield return new WaitForSeconds(2.5f); yield return Shot("later");
            Check("hitscan or missile events fired", Count(ZEventType.Hitscan) + Count(ZEventType.MissileFired) > 0);
            Clear();
        }

        IEnumerator ExplosionGrid(Vector2Int home)
        {
            var area = Open(home, 12, 5);
            int y = area.yMin + 2;
            var items = new List<(string label, Func<ZObject> make)>
            {
                ("grenade", null),
                ("robot", () => Spawn("robot", "grunt", area.xMin + 2, y, Team.Blue, 1)),
                ("gun", () => Spawn("cannon", "gatling", area.xMin + 4, y, Team.Blue)),
                ("jeep", () => Spawn("vehicle", "jeep", area.xMin + 6, y, Team.Blue)),
                ("light_tank", () => Spawn("vehicle", "light", area.xMin + 8, y, Team.Blue)),
                ("heavy_tank", () => Spawn("vehicle", "heavy", area.xMin + 10, y, Team.Blue)),
            };
            var objs = new List<ZObject>();
            foreach (var it in items) objs.Add(it.make?.Invoke());
            var centre = TileWorld(new Vector2Int(area.xMin + 6, y));
            Rts(centre, 34f); yield return Settle(2f);
            for (int i = 0; i < items.Count; i++)
            {
                if (i == 0) Blast((area.xMin + 1) * 16 + 8, y * 16 + 8, 1, 8, true);
                else if (objs[i] != null) Kill(objs[i]); else { Note("explosion grid: no " + items[i].label); continue; }
                yield return new WaitForSeconds(0.35f); yield return Shot(items[i].label);
                yield return new WaitForSeconds(0.6f);
            }
            yield return new WaitForSeconds(1.5f); yield return Shot("aftermath");
            Check("explosions seen", Count(ZEventType.MissileExploded) + Count(ZEventType.Death) > 0);
            Clear();
        }

        static readonly string[] VehicleList = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane" };

        IEnumerator Vehicles(Vector2Int home)
        {
            var area = Open(home, 12, 7);
            var vs = new List<ZObject>();
            for (int i = 0; i < VehicleList.Length; i++) vs.Add(Spawn("vehicle", VehicleList[i], area.xMin + 1 + (i % 4) * 3, area.yMin + 1 + (i / 4) * 3, Team.Red));
            yield return Settle(0.5f);
            // a turn: down, then across (each vehicle its own lane)
            for (int i = 0; i < vs.Count; i++)
            {
                var v = vs[i]; if (v == null) continue;
                var wp = new List<ZWaypoint>
                {
                    new ZWaypoint { mode = WpMode.Move, refId = -1, x = (area.xMin + 11 - (i % 4) * 3) * 16 + 8, y = (area.yMin + 1 + (i / 4) * 3) * 16 + 8, playerGiven = true },
                    new ZWaypoint { mode = WpMode.Move, refId = -1, x = (area.xMin + 11 - (i % 4) * 3) * 16 + 8, y = (area.yMin + 6) * 16 + 8, playerGiven = true },
                };
                W.SendWaypoints(Team.Red, v, wp);
            }
            var centre = TileWorld(new Vector2Int(area.xMin + 6, area.yMin + 3));
            Rts(centre, 40f); yield return new WaitForSeconds(2.0f); yield return Shot("driving");
            var jeep = vs[0];
            if (jeep != null) { var p = WPos(jeep); Close(p + Vector3.up, 11f, 130f, 28f); yield return new WaitForSeconds(0.25f); yield return Shot("jeep_close"); }
            var tank = vs.Count > 3 ? vs[3] : null;
            if (tank != null) { var p = WPos(tank); Close(p + Vector3.up, 14f, 220f, 30f); yield return new WaitForSeconds(0.25f); yield return Shot("heavy_close"); }
            FreeCam(false);
            yield return new WaitForSeconds(3.5f); yield return Shot("turned");
            int moved = 0; for (int i = 0; i < vs.Count; i++) { var v = vs[i]; if (v != null && Mathf.Abs(v.CenterX / 16 - (area.xMin + 1 + (i % 4) * 3)) + Mathf.Abs(v.CenterY / 16 - (area.yMin + 1 + (i / 4) * 3)) >= 2) moved++; }
            Check($"vehicles moved ({moved}/{vs.Count})", moved >= vs.Count - 1);
            Clear();
        }

        IEnumerator Boarding(Vector2Int home)
        {
            var area = Open(home, 12, 7);
            var jeep = Spawn("vehicle", "jeep", area.xMin + 1, area.yMin + 1, Team.Neutral);
            var tank = Spawn("vehicle", "light", area.xMin + 5, area.yMin + 1, Team.Neutral);
            var gun = Spawn("cannon", "gatling", area.xMin + 10, area.yMin + 1, Team.Neutral);
            var g1 = Spawn("robot", "grunt", area.xMin + 1, area.yMin + 5, Team.Red, 1);
            var g2 = Spawn("robot", "grunt", area.xMin + 5, area.yMin + 5, Team.Red, 1);
            var g3 = Spawn("robot", "grunt", area.xMin + 10, area.yMin + 5, Team.Red, 1);
            var centre = TileWorld(new Vector2Int(area.xMin + 6, area.yMin + 3));
            Rts(centre, 32f); yield return Settle(1f); yield return Shot("before");
            Order(g1, jeep, WpMode.Enter); Order(g2, tank, WpMode.Enter); Order(g3, gun, WpMode.Enter);
            if (jeep != null) { yield return new WaitForSeconds(1.6f); Close(WPos(jeep) + Vector3.up, 10f, 160f, 28f); yield return new WaitForSeconds(0.3f); yield return Shot("entering_jeep"); FreeCam(false); }
            yield return new WaitForSeconds(3f); yield return Shot("boarded");
            Check("Entered events (3 expected)", Count(ZEventType.Entered) >= 2);
            Check("jeep now red", jeep != null && jeep.Owner == Team.Red);
            { float tw = 0; while (tw < 10f && tank != null && tank.Owner != Team.Red) { tw += Time.deltaTime; yield return null; } }
            if (tank != null && tank.Owner == Team.Red) { W.Eject(Team.Red, tank); yield return new WaitForSeconds(0.8f); yield return Shot("eject_tank"); }
            Check("Ejected event", Count(ZEventType.Ejected) >= 1);
            yield return new WaitForSeconds(1f);
            Clear();
        }

        IEnumerator Snipe(Vector2Int home)
        {
            var area = Open(home, 12, 7);
            var jeep = Spawn("vehicle", "jeep", area.xMin + 1, area.yMin + 1, Team.Blue);
            var sn = Spawn("robot", "sniper", area.xMin + 9, area.yMin + 5, Team.Red, 2);
            var centre = TileWorld(new Vector2Int(area.xMin + 5, area.yMin + 3));
            Rts(centre, 30f); yield return Settle(0.5f);
            Order(sn, jeep, WpMode.Attack);
            float t = 0; while (t < 25f && jeep != null && jeep.Owner == Team.Blue && !jeep.Destroyed) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.25f); yield return Shot("driver_out");
            yield return new WaitForSeconds(1.2f); yield return Shot("ragdoll");
            Check($"jeep sniped (neutral) after {t:F1} s", jeep != null && jeep.Owner == Team.Neutral && !jeep.Destroyed);
            Check("Sniped event", Count(ZEventType.Sniped) >= 1);
            if (jeep != null && jeep.Owner == Team.Neutral && sn != null && !sn.Destroyed)
            {
                Order(sn, jeep, WpMode.Enter);
                float t2 = 0; while (t2 < 15f && jeep.Owner != Team.Red) { t2 += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(0.4f); yield return Shot("recaptured");
                Check($"recaptured by walking in ({t2:F1} s)", jeep.Owner == Team.Red);
            }
            Clear();
        }

        IEnumerator FlagCapture(Vector2Int home)
        {
            var flag = Nearest(home, o => o.Type == "flag" && o.Owner != Team.Red);
            if (flag == null) { Note("no capturable flag"); yield break; }
            var ft = T(flag);
            var start = Open(ft, 2, 2, 2);
            var g = Spawn("robot", "grunt", start.xMin, start.yMin, Team.Red, 1);
            Rts(WPos(flag), 40f); yield return Settle(1.5f); yield return Shot("before");
            var before = flag.Owner;
            Move(g, ft);
            float t = 0; while (t < 20f && flag.Owner != Team.Red) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.4f); yield return Shot("capture");
            yield return new WaitForSeconds(1.5f); yield return Shot("zone_tint");
            Close(WPos(flag) + Vector3.up * 1.5f, 8f, 200f, 15f); yield return new WaitForSeconds(0.5f); yield return Shot("flag_close");
            Check($"flag {before} -> Red in {t:F1} s", flag.Owner == Team.Red);
            Check("FlagCaptured event", Count(ZEventType.FlagCaptured) >= 1);
        }

        /// <summary>
        /// stored, PLACE (placement mode, an invalid tile refused + its error sound, a valid tile), deploy (CannonPlaced, crew, view,
        /// placed sound) and the cannon fires at an enemy squad.</summary>
        IEnumerator CannonFlow(Vector2Int home)
        {
            var fac = Nearest(home, o => (o.Type == "robot_factory" || o.Type == "vehicle_factory") && o.Owner == Team.Red)
                   ?? Nearest(home, o => (o.Type == "robot_factory" || o.Type == "vehicle_factory") && o.Owner != Team.Blue);
            if (fac == null) { Note("no factory"); Check("cannon flow: a red factory", false); yield break; }
            if (fac.Owner != Team.Red)
            {
                int z = W.GetZoneIndex(fac.CenterX, fac.CenterY);
                var flag = Find(o => o.Type == "flag" && W.GetZoneIndex(o.CenterX, o.CenterY) == z);
                if (flag != null) W.AwardZone(flag, Team.Red);
            }
            // 1 produce: the first cannon type this factory can build
            string cannon = null;
            foreach (var c in new[] { "gatling", "gun", "howitzer", "missile_cannon" }) if (W.StartBuilding(Team.Red, fac, c)) { cannon = c; break; }
            Check("cannon flow: factory accepts a cannon order", cannon != null);
            if (cannon == null) yield break;
            int zoneCannons = W.CannonsInZone(fac);
            if (zoneCannons >= ZObject.MAX_STORED_CANNONS) Note($"zone already holds {zoneCannons} cannons: Zod discards a 5th (zbuilding.cpp:429-452)");
            fac.BFinal = W.Time + 5.0; fac.BInit = W.Time - 20.0;                        // staged: finish in 5 s
            var p = WPos(fac); Rts(p, 30f);
            float t = 0; while (t < 12f && Count(ZEventType.CannonStored) == 0) { t += Time.deltaTime; yield return null; }
            yield return Shot("cannon_stored");
            Check($"cannon flow: {cannon} produced and stored ({fac.StoredCannons.Count} stored)", Count(ZEventType.CannonStored) >= 1 && fac.StoredCannons.Count > 0);
            if (fac.StoredCannons.Count == 0) yield break;
            // 2 place: placement mode on, one refused tile, one valid tile inside the zone
            var input = FindFirstObjectByType<PlayerInput>();
            if (input) input.BeginPlaceCannon(fac);
            Check("cannon flow: placement mode opens", input != null && input.Placing == fac);
            Vector2Int valid = new Vector2Int(-1, -1), invalid = new Vector2Int(-1, -1); var ft = T(fac);
            for (int r = 3; r < 14 && (valid.x < 0 || invalid.x < 0); r++)
                for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    int x = ft.x + dx, y = ft.y + dy; bool ok = W.CannonPlacable(fac, x, y);
                    if (ok && valid.x < 0) valid = new Vector2Int(x, y); if (!ok && invalid.x < 0) invalid = new Vector2Int(x, y);
                }
            Check("cannon flow: a valid and an invalid tile near the factory", valid.x >= 0 && invalid.x >= 0);
            if (valid.x < 0) { if (input) input.CancelPlace(); yield break; }
            yield return new WaitForSeconds(0.6f); yield return Shot("placement_ghost");
            var refused = invalid.x >= 0 ? W.PlaceCannon(Team.Red, fac, invalid.x, invalid.y) : null;
            Check("cannon flow: the invalid tile is refused + error sound exists", refused == null && AudioDirector.Has("ui_cannon_place_invalid"));
            if (refused == null) AudioDirector.UI("ui_cannon_place_invalid");
            float t0 = Time.unscaledTime;
            var gun = W.PlaceCannon(Team.Red, fac, valid.x, valid.y);
            if (input) input.CancelPlace();
            Check($"cannon flow: placed on {valid}", gun != null);
            if (gun == null) yield break;
            // 3 deploy: event, crew, view, sound
            Rts(WPos(gun), 22f); yield return new WaitForSeconds(0.4f); yield return Shot("deploy_pop");
            yield return new WaitForSeconds(1.2f); yield return Shot("deployed");
            bool view = false; foreach (var v in FindObjectsByType<UnitView>(FindObjectsSortMode.None)) if (v.Obj == gun) { view = true; break; }
            bool placedSound = false; foreach (var e in OzSoundLog.Since(t0)) if (e.outcome.StartsWith("PLAY") && (e.key.Contains("placed") || e.key.Contains("deploy"))) placedSound = true;
            Check("cannon flow: CannonPlaced event + view + crewed + placed sound",
                  Count(ZEventType.CannonPlaced) >= 1 && view && gun.Owner == Team.Red && !string.IsNullOrEmpty(gun.DriverType) && placedSound);
            if (!placedSound) Note("no placed / deploy sound in the sound log");
            // 4 fires: a blue squad walks into range
            var gt = T(gun);
            var enemy = Spawn("robot", "grunt", gt.x + 4, gt.y + 1, Team.Blue, 2);
            float w = 0; int shots0 = Count(ZEventType.Hitscan) + Count(ZEventType.MissileFired);
            while (w < 10f && gun != null && gun.AttackObject == null) { w += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.5f); yield return Shot("cannon_fires");
            Check($"cannon flow: the {cannon} engages ({w:F1} s) and fires", gun != null && (gun.AttackObject != null || Count(ZEventType.Hitscan) + Count(ZEventType.MissileFired) > shots0));
            Clear();
        }

        IEnumerator Factory(Vector2Int home)
        {
            var fac = Nearest(home, o => o.Type == "robot_factory" && o.Owner != Team.Blue) ?? Nearest(home, o => o.Type == "vehicle_factory" && o.Owner != Team.Blue);
            if (fac == null) { Note("no factory"); yield break; }
            if (fac.Owner != Team.Red)
            {
                int z = W.GetZoneIndex(fac.CenterX, fac.CenterY);
                var flag = Find(o => o.Type == "flag" && W.GetZoneIndex(o.CenterX, o.CenterY) == z);
                if (flag != null) W.AwardZone(flag, Team.Red); else Note("factory zone flag not found");
            }
            string unit = fac.Type == "robot_factory" ? "grunt" : "jeep";
            if (!W.StartBuilding(Team.Red, fac, unit)) foreach (var u in new[] { "psycho", "tough", "light", "sniper", "pyro", "medium" }) if (W.StartBuilding(Team.Red, fac, u)) { unit = u; break; }
            fac.BFinal = W.Time + 5.0; fac.BInit = W.Time - 20.0;                    // staged: finish in 5 s (the real formula is checked by the parity suite)
            var rally = Open(T(fac), 2, 2, 5);
            W.SetRallyPoints(Team.Red, fac, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = rally.xMin * 16 + 8, y = rally.yMin * 16 + 8, playerGiven = true } });
            var p = WPos(fac);
            Rts(p, 30f); yield return Settle(1.5f); yield return Shot("producing_timer");
            // v3: the door camera stands out in front of the exit (GetCreationPoint) and looks back at the door
            Vector3 door = p; if (fac.GetCreationPoint(out int ex, out int ey)) { door = ZGame.PxToWorld(ex, ey); door.y = G.GroundY(door.x, door.z); }
            var outDir = door - p; outDir.y = 0; if (outDir.sqrMagnitude < 0.01f) outDir = Vector3.back; outDir.Normalize();
            float doorYaw = Mathf.Atan2(-outDir.x, -outDir.z) * Mathf.Rad2Deg;                   // camera looks from outside toward the building
            Close(door + Vector3.up * 1.8f - outDir * 1.5f, 13f, doorYaw, 24f); yield return new WaitForSeconds(0.5f); yield return Shot("door_before");
            float t = 0; while (t < 12f && Count(ZEventType.UnitBuilt) == 0) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.25f); yield return Shot("door_open");
            yield return new WaitForSeconds(1.0f); yield return Shot("unit_rolls_out");
            yield return new WaitForSeconds(1.2f); yield return Shot("unit_out");
            FreeCam(false); Rts(p, 30f);
            yield return new WaitForSeconds(3f); yield return Shot("rally");
            Check($"UnitBuilt ({unit}) at {fac.Type}", Count(ZEventType.UnitBuilt) >= 1);
        }

        IEnumerator RocksSandbags(Vector2Int home)
        {
            var rock = Nearest(home, o => o.Type == "rock" && o.MaxHealth > 0);
            if (rock != null)
            {
                var p = WPos(rock);
                Rts(p, 28f); yield return Settle(1.5f); yield return Shot("rock_before");
                Blast(rock.CenterX, rock.CenterY, 6000, 12);
                yield return new WaitForSeconds(0.35f); yield return Shot("rock_blast");
                yield return new WaitForSeconds(2f); yield return Shot("rock_rubble");
                Check("rock destroyed", rock.Destroyed || rock.KillAt >= 0);
            }
            else Note("no destructible rock");
            var sb = FindFirstObjectByType<SandbagBreakables>();
            Bounds? bag = null;
            if (sb)
            {
                var segs = typeof(SandbagBreakables).GetField("segs", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(sb) as IList;
                if (segs != null)
                {
                    var redP = TileWorld(home); float bd = float.MaxValue;
                    foreach (var s in segs)
                    {
                        var st = s.GetType(); if ((bool)st.GetField("broken").GetValue(s)) continue;
                        var b = (Bounds)st.GetField("b").GetValue(s); float d = (b.center - redP).sqrMagnitude;
                        if (d < bd) { bd = d; bag = b; }
                    }
                }
            }
            if (bag.HasValue)
            {
                var c = bag.Value.center; var px = ZGame.WorldToPx(c);
                Rts(c, 24f); yield return Settle(1.5f); yield return Shot("sandbag_before");
                Blast(px.x, px.y, 1, 8, true);
                yield return new WaitForSeconds(0.35f); yield return Shot("sandbag_blast");
                yield return new WaitForSeconds(2f); yield return Shot("sandbag_rubble");
            }
            else Note("no sandbag segment found");
        }

        IEnumerator BuildingKill(Vector2Int home)
        {
            var b = Nearest(home, o => o.Kind == ZKind.Building && o.Owner != Team.Red && (o.Type == "radar" || o.Type == "repair"))
                 ?? Nearest(home, o => o.Kind == ZKind.Building && o.Owner == Team.Neutral && o.Type.Contains("factory"));
            if (b == null) { Note("no building to destroy"); yield break; }
            var p = WPos(b);
            Rts(p, 34f); yield return Settle(1.5f); yield return Shot("before");
            Kill(b);
            yield return new WaitForSeconds(0.3f); yield return Shot("blast");
            yield return new WaitForSeconds(1.2f); yield return Shot("breakup");
            yield return new WaitForSeconds(3.5f); yield return Shot("settled");
            Check($"{b.Type} destroyed", b.Destroyed);
        }

        IEnumerator Wading(Vector2Int home)
        {
            // the nearest water tile with ground within 6 tiles on two opposite sides
            Vector2Int? a = null, bT = null; float bd = float.MaxValue;
            for (int y = 2; y < W.Map.height - 2; y++)
                for (int x = 2; x < W.Map.width - 2; x++)
                {
                    if (W.Map.ClassAt(x, y) != ZClass.Water) continue;
                    float d = (new Vector2Int(x, y) - home).sqrMagnitude; if (d >= bd) continue;
                    foreach (var dir in new[] { Vector2Int.right, Vector2Int.up })
                    {
                        Vector2Int? s1 = null, s2 = null;
                        for (int k = 1; k <= 6 && s1 == null; k++) { var q = new Vector2Int(x, y) - dir * k; if (TileFree(q.x, q.y) && W.Map.ClassAt(q.x, q.y) == ZClass.Ground) s1 = q - dir; }
                        for (int k = 1; k <= 6 && s2 == null; k++) { var q = new Vector2Int(x, y) + dir * k; if (TileFree(q.x, q.y) && W.Map.ClassAt(q.x, q.y) == ZClass.Ground) s2 = q + dir; }
                        if (s1 != null && s2 != null) { bd = d; a = s1; bT = s2; break; }
                    }
                }
            if (a == null) { Note("no wading crossing found"); yield break; }
            var g = Spawn("robot", "grunt", a.Value.x, a.Value.y, Team.Red);
            Move(g, bT.Value);
            var mid = (TileWorld(a.Value) + TileWorld(bT.Value)) * 0.5f;
            Rts(mid, 26f);
            float t = 0; bool wet = false;
            while (t < 25f && g != null && !g.Destroyed)
            {
                t += Time.deltaTime;
                if (!wet && W.Map.ClassAt(g.CenterX / 16, g.CenterY / 16) == ZClass.Water) { wet = true; yield return new WaitForSeconds(0.5f); yield return Shot("wading"); Close(WPos(g) + Vector3.up, 10f, 150f, 28f); yield return new WaitForSeconds(0.3f); yield return Shot("wading_close"); FreeCam(false); }
                if (wet && W.Map.ClassAt(g.CenterX / 16, g.CenterY / 16) != ZClass.Water) break;
                yield return null;
            }
            Check("robots entered the water", wet);
            yield return new WaitForSeconds(1f); yield return Shot("across");
            Clear();
        }

        IEnumerator Critters()
        {
            var al = AmbientLife.I;
            if (!al) { Note("no AmbientLife"); yield break; }
            var f = BindingFlags.NonPublic | BindingFlags.Instance;
            var worms = typeof(AmbientLife).GetField("wormList", f)?.GetValue(al) as IList;
            var birds = typeof(AmbientLife).GetField("birds", f)?.GetValue(al) as IList;
            if (worms != null && worms.Count > 0)
            {
                var w = worms[0];
                typeof(AmbientLife).GetMethod("Emerge", f)?.Invoke(al, new[] { w });
                yield return new WaitForSeconds(1.8f);
                var p = (Vector3)w.GetType().GetField("pos").GetValue(w);
                Close(p + Vector3.up * 1.5f, 14f, 200f, 30f); yield return new WaitForSeconds(0.4f); yield return Shot("worm");
            }
            if (birds != null && birds.Count > 0)
            {
                var b = birds[0]; var p = (Vector3)b.GetType().GetField("pos").GetValue(b);
                Close(p, 12f, 200f, 30f); yield return new WaitForSeconds(0.2f); yield return Shot("bird");
            }
            Note($"worms {worms?.Count ?? 0}, birds {birds?.Count ?? 0}");
        }

        IEnumerator Grenades(Vector2Int home)
        {
            var crate = Nearest(home, o => o.Type == "grenades");
            if (crate == null) { Note("no grenade crate"); yield break; }
            var near = Open(T(crate), 2, 2, 3);
            var g = Spawn("robot", "grunt", near.xMin, near.yMin, Team.Red);
            var p = WPos(crate);
            Close(p + Vector3.up * 0.8f, 9f, 200f, 25f); yield return Settle(1f); yield return Shot("crate");
            FreeCam(false); Rts(p, 26f);
            Order(g, crate, WpMode.PickupGrenades);
            float t = 0; while (t < 15f && Count(ZEventType.GrenadesPicked) == 0) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f); yield return Shot("picked");
            Check($"GrenadesPicked ({t:F1} s), grenades {g?.Grenades}", Count(ZEventType.GrenadesPicked) >= 1);
            // throw: an enemy squad in grenade range
            var enemyAt = Open(T(g ?? crate), 2, 2, 4);
            var e = Spawn("robot", "tough", enemyAt.xMin, enemyAt.yMin, Team.Blue, 1);
            Order(g, e, WpMode.Attack);
            t = 0; bool thrown = false;
            while (t < 12f && !thrown) { t += Time.deltaTime; foreach (var s in cur.events) if (s.Contains("MissileFired") && s.Contains("Red grunt")) thrown = true; yield return null; }
            yield return new WaitForSeconds(0.4f); yield return Shot("throw");
            yield return new WaitForSeconds(0.9f); yield return Shot("grenade_blast");
            Clear();
        }

        IEnumerator Tracks(Vector2Int home)
        {
            var area = Open(home, 12, 6);
            var j = Spawn("vehicle", "jeep", area.xMin + 1, area.yMin + 1, Team.Red);
            var t = Spawn("vehicle", "light", area.xMin + 1, area.yMin + 3, Team.Red);
            var r = Spawn("robot", "grunt", area.xMin + 1, area.yMin + 5, Team.Red);
            Move(j, new Vector2Int(area.xMax - 2, area.yMin + 1)); Move(t, new Vector2Int(area.xMax - 2, area.yMin + 3)); Move(r, new Vector2Int(area.xMax - 2, area.yMin + 5));
            var centre = TileWorld(new Vector2Int(area.xMin + 6, area.yMin + 3));
            Rts(centre, 30f);
            yield return new WaitForSeconds(3f); yield return Shot("dust");
            yield return new WaitForSeconds(4f); yield return Shot("tracks");
            Clear();
        }

        IEnumerator NightLights(Vector2Int home)
        {
            SetTime(0.62f);
            yield return new WaitForSeconds(3f);
            var area = Open(home, 10, 4);
            var j = Spawn("vehicle", "jeep", area.xMin + 1, area.yMin + 1, Team.Red);
            var tk = Spawn("vehicle", "light", area.xMin + 1, area.yMin + 3, Team.Red);
            Move(j, new Vector2Int(area.xMax - 1, area.yMin + 1)); Move(tk, new Vector2Int(area.xMax - 1, area.yMin + 3));
            Rts(TileWorld(new Vector2Int(area.xMin + 5, area.yMin + 2)), 30f); yield return new WaitForSeconds(2f); yield return Shot("headlights");
            var fort = Find(o => o.Type.StartsWith("fort") && o.Owner == Team.Red);
            if (fort != null) { Rts(WPos(fort), 45f); yield return Settle(2f); yield return Shot("red_fort"); }
            var bfort = Find(o => o.Type.StartsWith("fort") && o.Owner == Team.Blue);
            if (bfort != null) { Rts(WPos(bfort), 45f); yield return Settle(2f); yield return Shot("blue_fort"); }
            var br = Find(o => o.Type.StartsWith("bridge"));
            if (br != null) { Rts(WPos(br), 40f); yield return Settle(2f); yield return Shot("bridge"); }
            var fac = Find(o => o.Type.Contains("factory") && o.Owner == Team.Red);
            if (fac != null) { Rts(WPos(fac), 35f); yield return Settle(2f); yield return Shot("factory"); }
            SetTime(0.08f);
            Clear();
        }

        IEnumerator BridgeRepair(Vector2Int home)
        {
            ZObject br = null; float bdist = float.MaxValue;
            foreach (var o in W.Objects) if (o.IsBridge && !o.Destroyed) { float dd = (T(o) - home).sqrMagnitude; if (dd < bdist) { bdist = dd; br = o; } }   // an intact one: the 120 s rule
            if (br == null) foreach (var o in W.Objects) if (o.IsBridge) { float dd = (T(o) - home).sqrMagnitude; if (dd < bdist) { bdist = dd; br = o; } }   // else a map-start broken one (Zod's 600-660 s)
            if (br == null) { Note("no bridge on this map"); yield break; }
            var p = WPos(br);
            Rts(p, 34f); yield return Settle(1.5f); yield return Shot(br.Destroyed ? "already_destroyed" : "intact");
            if (br.Destroyed)
            {   // L17: both bridges start the level destroyed (Zod's 600-660 s crane design); rebuild one first (the crane's call),
                // so the 120 s OpenZ rule for a bridge destroyed in play can be watched
                W.AutoRepair(br); Note("map-start broken bridge rebuilt first (AutoRepair, as a crane would)");
                yield return new WaitForSeconds(1.5f); yield return Shot("rebuilt_first");
            }
            Kill(br);
            yield return new WaitForSeconds(0.4f); yield return Shot("blast");
            yield return new WaitForSeconds(2.5f); yield return Shot("destroyed_badge");
            Check("bridge destroyed", br.Destroyed);
            G.gameSpeed = 4f;
            double killT = W.Time; float t = 0;
            while (W.Time - killT < 140.0 && br.Destroyed && t < 240f) { t += Time.deltaTime; yield return null; }   // sim time (pauses do not count)
            G.gameSpeed = 1f;
            yield return new WaitForSeconds(0.5f); yield return Shot("rebuilt");
            Check($"bridge auto-repaired (sim {W.Time - cur.sim0:F0} s)", !br.Destroyed);
        }

        // ------------------------------------------------------------------ UI
        IEnumerator UiSelection()
        {
            if (!input) { Note("no PlayerInput"); yield break; }
            var home = T(Find(o => o.Kind == ZKind.Building && o.Owner == Team.Red && o.Type.StartsWith("fort")) ?? W.Objects[0]);
            var area = Open(home, 12, 6, 12);
            var list = new List<ZObject>();
            for (int i = 0; i < RobotTypeList.Length; i++) list.Add(Spawn("robot", RobotTypeList[i], area.xMin + 1 + i * 2, area.yMin + 1, Team.Red));
            list.Add(Spawn("vehicle", "medium", area.xMin + 3, area.yMin + 4, Team.Red));
            list.Add(Spawn("cannon", "gatling", area.xMin + 8, area.yMin + 4, Team.Red));
            Rts(TileWorld(new Vector2Int(area.xMin + 6, area.yMin + 3)), 30f); yield return Settle(1.2f);
            foreach (var o in list)
            {
                if (o == null) continue;
                input.Selection.Clear(); input.Selection.Add(o); foreach (var m in o.Minions) input.Selection.Add(m);
                yield return new WaitForSeconds(0.5f); yield return Shot(o.Type);
            }
            // group bar: three groups
            var groups = typeof(PlayerInput).GetField("groups", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(input) as Dictionary<int, List<ZObject>>;
            if (groups != null)
            {
                groups[1] = new List<ZObject> { list[0] }; groups[2] = new List<ZObject> { list[2], list[4] }; if (list[6] != null) groups[3] = new List<ZObject> { list[6] };
                input.Selection.Clear(); input.Selection.AddRange(new[] { list[0], list[1], list[2] });
                yield return new WaitForSeconds(0.5f); yield return Shot("groups_multi");
            }
            input.Selection.Clear();
        }

        IEnumerator UiProduction(ZObject fort)
        {
            if (!hud || hud.Production == null || fort == null) { Note("no production window / fort"); yield break; }
            Rts(WPos(fort), 45f); yield return Settle(1f);
            hud.Production.Open(fort); yield return new WaitForSeconds(0.8f); yield return Shot("fort_window");
            hud.Production.Close();
            var fac = Find(o => o.Owner == Team.Red && o.Type.Contains("factory"));
            if (fac != null) { Rts(WPos(fac), 40f); yield return Settle(1f); hud.Production.Open(fac); yield return new WaitForSeconds(0.8f); yield return Shot("factory_window"); hud.Production.Close(); }
            Clear();
        }

        IEnumerator UiPause()
        {
            if (!hud || hud.Pause == null) { Note("no pause menu"); yield break; }
            hud.Pause.Open(); yield return new WaitForSecondsRealtime(1.0f); yield return Shot("pause");
            hud.Pause.Close(); yield return new WaitForSecondsRealtime(0.6f);
        }

        IEnumerator UiEnd()
        {
            var es = hud ? hud.EndScreen : null;
            if (!es) { Note("no end screen"); yield break; }
            var show = typeof(OpenZEndScreen).GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance);
            var shown = typeof(OpenZEndScreen).GetProperty("IsShown");
            show.Invoke(es, new object[] { Team.Red }); yield return new WaitForSecondsRealtime(4f); yield return Shot("victory");
            shown.GetSetMethod(true)?.Invoke(es, new object[] { false });
            show.Invoke(es, new object[] { Team.Blue }); yield return new WaitForSecondsRealtime(4f); yield return Shot("defeat");
        }

        // ------------------------------------------------------------------ main menu pass
        IEnumerator MenuPass()
        {
            Setup();
            cur = new Step { n = ++stepNo, name = "main_menu" }; steps.Add(cur);
            yield return new WaitForSecondsRealtime(2f); yield return Shot("t2s");
            yield return new WaitForSecondsRealtime(3f); yield return Shot("t5s");
            var mm = FindFirstObjectByType<OpenZMainMenu>();
            var f = BindingFlags.NonPublic | BindingFlags.Instance;
            cur = new Step { n = ++stepNo, name = "level_select" }; steps.Add(cur);
            if (mm) { try { typeof(OpenZMainMenu).GetMethod("GoSelect", f)?.Invoke(mm, null); } catch (Exception ex) { Fail(ex); } }
            else Note("no OpenZMainMenu");
            yield return new WaitForSecondsRealtime(3f); yield return Shot("");
            yield return new WaitForSecondsRealtime(5f); yield return Shot("preview_later");
        }

        // ------------------------------------------------------------------ log
        void WriteLog()
        {
            if (string.IsNullOrEmpty(outDir)) return;
            var sb = new StringBuilder();
            sb.Append("{\n \"level\": \"").Append(level).Append("\",\n \"screen\": \"").Append(Screen.width).Append('x').Append(Screen.height)
              .Append("\",\n \"time\": \"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",\n \"steps\": [\n");
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                sb.Append("  {\"n\": ").Append(s.n).Append(", \"name\": \"").Append(s.name).Append("\", \"failed\": ").Append(s.failed ? "true" : "false")
                  .Append(", \"real_s\": ").Append((s.t1 - s.t0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(", \"sim_s\": ").Append((s.sim1 - s.sim0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("   \"frames\": [").Append(Join(s.frames)).Append("],\n");
                sb.Append("   \"checks\": [").Append(Join(s.checks)).Append("],\n");
                sb.Append("   \"notes\": [").Append(Join(s.notes)).Append("],\n");
                sb.Append("   \"events\": [\n    ").Append(string.Join(",\n    ", s.events)).Append("]}").Append(i < steps.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(" ]\n}\n");
            try { File.WriteAllText(Path.Combine(outDir, "gate_log.json"), sb.ToString()); } catch (Exception ex) { Debug.LogException(ex); }
        }

        static string Join(List<string> l)
        {
            var parts = new List<string>();
            foreach (var s in l) parts.Add("\"" + s.Replace("\\", "/").Replace("\"", "'") + "\"");
            return string.Join(", ", parts);
        }
    }
}
