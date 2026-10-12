#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// production_badge_v2 side-by-side proof captures (before / after the badge import). Menu OpenZ / UI / Production
    /// Badge / Proof Captures (Play), in Play on L02 or L17 with the Game view at 2560x1600 and again at 1920x1080. UI SCALE: the HUD
    /// resolves it ONCE when the level starts (PlayerPrefs oz_ui_scale wins, else the default for the screen height: 1.35 at 1440 px
    /// and up, 1.0 below), so set the Game view resolution BEFORE entering Play, delete oz_ui_scale first, and restart Play for each
    /// resolution; the tool warns in the log and in index.md when the scale is not the default or the canvas is not 1 or 2 px per u.
    ///
    /// It needs no other part of the package, so apply.py --proof-only installs it alone BEFORE the badge: run it once on the old badge
    /// ("before") and again after the import ("after"); the phase is detected (OzBadgeState present = after) and the camera poses are
    /// the same both times, picked from the level itself: your fort, robot factory and vehicle factory, the enemy fort and factory, a
    /// neutral factory, at the default RTS height 70 m; your fort at the closest 22 m and at 34 m; the 160 m and the full 230 m
    /// overview (enemy compact form, neighbour nudge, level tags); your fort with its production window open (family shot: the
    /// window's time and TECH pips must equal the badge's; the tool logs which unit is building and asks for a tough, laser or pyro);
    /// your fort with the nearest own units selected (brackets, HP bars, the door); your fort at the right screen edge with its anchor
    /// just inside and just outside (the edge rule: kept inside, then sliding off and fading); and a night pass (time of day 0.65).
    /// After the import each own pose is also shot in the forced, view-only states (OzBadgeState.Force: READY at 0.00 / 0.10 / 0.40 /
    /// 1.08 s, PAUSED, PLACE while producing, PLACE with nothing in production, SELECT); enemy poses PAUSED. The Sim is never touched.
    /// Per producer on screen: the full frame, a 1:1 and a 3x crop around the badge's REAL rect (OpenZUnitOverlay.BadgeRects, grown to
    /// a fixed minimum box so the old badge fits too), and when the "before" frame of the same pose exists, the before frame is cut to
    /// &lt;scene&gt;_&lt;W&gt;x&lt;H&gt;/ + index_&lt;phase&gt;.md (UI scale, tech bonus, every role's level). Camera (edge scroll off), time of day,
    /// production window, selection and the force state are restored afterwards.
    /// </summary>
    public static class OzProductionBadgeProof
    {
        sealed class Shot { public string name, focusRole, state; public float height, tod = -1f, readyT; public bool window, select; public int edge; }
        sealed class Role { public string name; public ZObject b; }

        static readonly List<Shot> shots = new List<Shot>();
        static readonly List<Role> roles = new List<Role>();
        static readonly StringBuilder index = new StringBuilder();
        static int si, waitUntil; static double started; static string dir, phase, shotPath; static bool shooting;
        static float edgePx, tod0 = -1f; static Vector3 focus0; static float height0; static bool dnRunning;
        static FieldInfo forceField, readyTField, rectsField; static Type forceType;
        static readonly List<ZObject> sel0 = new List<ZObject>();

        [MenuItem("OpenZ/UI/Production Badge/Proof Captures (Play)")]
        static void Start()
        {
            if (!Application.isPlaying) { Debug.LogError("[BadgeProof] enter Play on L02 or L17 first"); return; }
            if (shooting) { Debug.LogWarning("[BadgeProof] already running"); return; }
            var hud = OpenZHud.I;
            if (!hud || hud.W == null || !hud.rts) { Debug.LogError("[BadgeProof] no HUD / world / RTS camera in this scene"); return; }
            var asm = typeof(OpenZHud).Assembly;
            var st = asm.GetType("OpenZ.OzBadgeState");
            forceField = st?.GetField("Force", BindingFlags.Public | BindingFlags.Static);
            readyTField = st?.GetField("ForceReadyT", BindingFlags.Public | BindingFlags.Static);
            rectsField = asm.GetType("OpenZ.OpenZUnitOverlay")?.GetField("BadgeRects", BindingFlags.Public | BindingFlags.Static);
            forceType = forceField?.FieldType;
            phase = forceField != null ? "after" : "before";
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            dir = Path.Combine(root, "work", "records", "milestones", "production_badge_v2", scene + "_" + Screen.width + "x" + Screen.height);
            Directory.CreateDirectory(dir);
            PickRoles(hud);
            BuildShots();
            var rts = hud.rts; edgePx = rts.edgePx; rts.edgePx = -1f; focus0 = rts.focus; height0 = rts.height;
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); if (dn) { tod0 = dn.timeOfDay; dnRunning = dn.running; dn.running = false; }
            sel0.Clear(); sel0.AddRange(hud.input.Selection);
            index.Clear();
            index.AppendLine("# Production badge proof: " + scene + " " + Screen.width + "x" + Screen.height + " (" + phase + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            index.AppendLine();
            float k = Screen.height / 1080f * OzText.UiScale;
            index.AppendLine("UI scale " + OzText.UiScale.ToString("0.00") + " (default for this height " + OzText.DefaultScale.ToString("0.00") + (PlayerPrefs.HasKey("oz_ui_scale") ? ", PlayerPrefs oz_ui_scale = " + PlayerPrefs.GetFloat("oz_ui_scale").ToString("0.00") : ", no oz_ui_scale pref") +
                             "), canvas px per u " + k.ToString("0.00") + ".");
            var warn = new List<string>();
            if (!(Screen.width == 2560 && Screen.height == 1600 || Screen.width == 1920 && Screen.height == 1080)) warn.Add("not 2560x1600 or 1920x1080: set the Game view to a real game resolution BEFORE entering Play");
            if (Mathf.Abs(OzText.UiScale - OzText.DefaultScale) > 0.001f) warn.Add("UI SCALE " + OzText.UiScale.ToString("0.00") + " is not this screen's default " + OzText.DefaultScale.ToString("0.00") + ": delete PlayerPrefs oz_ui_scale, set the resolution, then restart Play (the HUD reads the scale once at level start)");
            if (Mathf.Abs(k - 1f) > 0.01f && Mathf.Abs(k - 2f) > 0.01f) warn.Add("canvas " + k.ToString("0.00") + " px per u: the badge is 1:1 only at 1.00 (1080p 100 %) and 2.00 (1600p 135 %); these shots show minified sprites");
            foreach (var w in warn) { index.AppendLine("WARNING: " + w); Debug.LogWarning("[BadgeProof] " + w); }
            var W = hud.W;
            index.AppendLine("Tech bonus (difficulty) red " + W.TechBonus[(int)Team.Red] + ", blue " + W.TechBonus[(int)Team.Blue] + ". Roles: " + RoleList());
            index.AppendLine();
            si = -1; shooting = true; started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            Next();
            Debug.Log("[BadgeProof] " + phase + ": " + shots.Count + " shots -> " + dir);
        }

        static string RoleList()
        {
            var sb = new StringBuilder();
            foreach (var r in roles) sb.Append(r.name).Append('=').Append(r.b.Type).Append('#').Append(r.b.Id).Append(" (level ").Append(r.b.Level).Append(", TECH ").Append(r.b.BuildLevel + 1).Append(") ");
            return sb.ToString();
        }

        static void PickRoles(OpenZHud hud)
        {
            roles.Clear();
            var me = hud.Player;
            void Pick(string name, Func<ZObject, bool> f)
            {
                ZObject best = null;
                foreach (var o in hud.W.Objects)
                    if (o.IsProducer && !o.Destroyed && f(o)) { if (best == null || (o.Building && !best.Building)) best = o; }
                if (best != null) roles.Add(new Role { name = name, b = best });
            }
            Pick("ownfort", o => o.Owner == me && o.IsFort);
            Pick("ownrobot", o => o.Owner == me && o.Type == "robot_factory");
            Pick("ownvehicle", o => o.Owner == me && o.Type == "vehicle_factory");
            Pick("enemyfort", o => o.Owner != me && o.Owner != Team.Neutral && o.IsFort);
            Pick("enemyfactory", o => o.Owner != me && o.Owner != Team.Neutral && !o.IsFort);
            Pick("neutralfactory", o => o.Owner == Team.Neutral && !o.IsFort);
        }

        static bool Has(string role) { foreach (var r in roles) if (r.name == role) return true; return false; }

        static void BuildShots()
        {
            shots.Clear();
            bool after = phase == "after";
            void Add(string name, string role, float h, float tod = 0.10f, bool window = false, bool forced = true, bool select = false, int edge = 0)
            {
                if (!Has(role)) return;
                shots.Add(new Shot { name = name, focusRole = role, height = h, tod = tod, window = window, select = select, edge = edge, state = "live" });
                if (!after || !forced) return;
                bool own = role.StartsWith("own", StringComparison.Ordinal);
                void F(string state, float readyT = 0f) => shots.Add(new Shot { name = name, focusRole = role, height = h, tod = tod, window = window, select = select, edge = edge, state = state, readyT = readyT });
                if (own)
                {
                    foreach (var t in new[] { 0.00f, 0.10f, 0.40f, 1.08f }) F("ready_" + Mathf.RoundToInt(t * 100).ToString("000"), t);
                    F("paused"); F("place"); F("placeidle"); F("idle");
                }
                else if (!role.StartsWith("neutral", StringComparison.Ordinal)) F("paused");
            }
            Add("ownfort_70", "ownfort", 70f);
            Add("ownfort_34", "ownfort", 34f);
            Add("ownfort_22", "ownfort", 22f, forced: false);
            Add("ownrobot_70", "ownrobot", 70f);
            Add("ownvehicle_70", "ownvehicle", 70f);
            Add("enemyfort_70", "enemyfort", 70f);
            Add("enemyfactory_70", "enemyfactory", 70f);
            Add("neutral_70", "neutralfactory", 70f, forced: false);
            Add("overview_160", "ownfort", 160f, forced: false);
            Add("overview_230", "enemyfort", 230f, forced: false);
            Add("window_70", "ownfort", 70f, window: true, forced: false);
            Add("selected_70", "ownfort", 70f, forced: false, select: true);
            Add("edge_in_70", "ownfort", 70f, forced: false, edge: 1);
            Add("edge_out_70", "ownfort", 70f, forced: false, edge: 2);
            Add("night_ownfort_70", "ownfort", 70f, 0.65f);
            Add("night_enemyfort_70", "enemyfort", 70f, 0.65f, forced: false);
        }

        static ZObject RoleB(string role) { foreach (var r in roles) if (r.name == role) return r.b; return null; }

        static void SetForce(string state)
        {
            if (forceField == null) return;
            string f = state.StartsWith("ready", StringComparison.Ordinal) ? "Ready" : state == "paused" ? "Paused" : state == "place" ? "Place" : state == "placeidle" ? "PlaceIdle" : state == "idle" ? "Idle" : "None";
            try { forceField.SetValue(null, Enum.Parse(forceType, f)); } catch (ArgumentException) { forceField.SetValue(null, Enum.Parse(forceType, "None")); }
        }

        static void Next()
        {
            si++;
            var hud = OpenZHud.I;
            if (si >= shots.Count || !hud) { Finish(null); return; }
            var s = shots[si];
            var b = RoleB(s.focusRole);
            if (b == null || b.Destroyed) { Next(); return; }
            var rts = hud.rts;
            var c = hud.game.CenterWorld(b);
            rts.focus = new Vector3(c.x, 0f, c.z);
            rts.SetHeightImmediate(s.height);
            if (s.edge > 0)
            {   // put the badge anchor at the right edge: 30 px inside (the badge is held inside) or 10 px outside (it slides off and fades)
                var cam = rts.GetComponent<Camera>(); var a = c + Vector3.up * 7f;
                var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
                float p0 = cam.WorldToScreenPoint(a).x, p1 = cam.WorldToScreenPoint(a + right * 10f).x, pxPerM = Mathf.Max(0.1f, (p1 - p0) / 10f);
                float target = s.edge == 1 ? Screen.width - 30f : Screen.width + 10f;
                rts.focus -= right * ((target - p0) / pxPerM);
                rts.SetHeightImmediate(s.height);
            }
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); if (dn && s.tod >= 0f) dn.timeOfDay = s.tod;
            if (hud.Production)
            {
                if (s.window && hud.Production.Building != b && b.Owner == hud.Player) hud.Production.Open(b);
                if (!s.window && hud.Production.IsOpen) hud.Production.Close();
            }
            var selection = hud.input.Selection;
            selection.Clear();
            if (s.select)
            {   // the nearest own units (the ones that just left the door): brackets, HP bars and hover chips next to the badge
                var near = new List<(float, ZObject)>();
                foreach (var o in hud.W.Objects)
                    if (o.IsUnit && !o.IsMinion && !o.Destroyed && o.KillAt < 0 && o.Owner == hud.Player) near.Add(((hud.game.CenterWorld(o) - c).sqrMagnitude, o));
                near.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                for (int i = 0; i < near.Count && i < 6; i++) selection.Add(near[i].Item2);
            }
            else selection.AddRange(sel0);
            if (s.window) index.AppendLine("- family shot: " + b.Type + " builds " + (b.CurUnit ?? "nothing") + (b.CurUnit == "tough" || b.CurUnit == "laser" || b.CurUnit == "pyro" ? "" : " (WARNING: pick TOUGH, LASER or PYRO in its window before the run, so the v4 card and the badge face are compared)"));
            SetForce(s.state);
            if (readyTField != null) readyTField.SetValue(null, s.readyT);
            bool bigMove = si == 0 || shots[si - 1].name != s.name;
            waitUntil = Time.frameCount + (bigMove ? 45 : 12);                                   // camera, lights and the badge fades settle
            shotPath = null;
        }

        static void Tick()
        {
            try
            {
                if (!Application.isPlaying) { Finish("left Play mode"); return; }
                if (EditorApplication.timeSinceStartup - started > 900) { Finish("timed out"); return; }
                if (Time.frameCount < waitUntil) return;
                var s = shots[si];
                if (shotPath == null)
                {
                    shotPath = Path.Combine(dir, $"{s.name}_{phase}_{s.state}.png");
                    if (File.Exists(shotPath)) File.Delete(shotPath);
                    SnapRects();
                    ScreenCapture.CaptureScreenshot(shotPath, 1);
                    waitUntil = Time.frameCount + 2;
                    return;
                }
                if (!File.Exists(shotPath)) { waitUntil = Time.frameCount + 1; return; }
                Crops(s);
                Next();
            }
            catch (Exception e) { Finish(e.ToString()); }
        }

        static readonly Dictionary<int, Rect> rects = new Dictionary<int, Rect>();
        static void SnapRects()
        {
            rects.Clear();
            if (rectsField?.GetValue(null) is Dictionary<int, Rect> d) foreach (var kv in d) rects[kv.Key] = kv.Value;
        }

        static Texture2D Load(string p)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { t.LoadImage(File.ReadAllBytes(p)); } catch (IOException) { return null; }
            return t;
        }

        /// <summary>The crop box of a producer (screen px, bottom-left origin): its real badge rect (after), grown by 12 u, and never
        /// smaller than the fixed box x -80..+150 u, y -64..+64 u around the anchor (the old badge fits too, before and after).</summary>
        static RectInt Box(ZObject o, Camera cam, float sf, int W, int H)
        {
            var sp = cam.WorldToScreenPoint(OpenZHud.I.game.CenterWorld(o) + Vector3.up * 7f);
            float x0 = sp.x - 80f * sf, x1 = sp.x + 150f * sf, y0 = sp.y - 64f * sf, y1 = sp.y + 64f * sf;
            if (rects.TryGetValue(o.Id, out var r))
            {
                x0 = Mathf.Min(x0, r.xMin - 12f * sf); x1 = Mathf.Max(x1, r.xMax + 12f * sf); y0 = Mathf.Min(y0, r.yMin - 12f * sf); y1 = Mathf.Max(y1, r.yMax + 12f * sf);
            }
            int ix0 = Mathf.Clamp(Mathf.RoundToInt(x0), 0, W - 1), ix1 = Mathf.Clamp(Mathf.RoundToInt(x1), ix0 + 1, W);
            int iy0 = Mathf.Clamp(Mathf.RoundToInt(y0), 0, H - 1), iy1 = Mathf.Clamp(Mathf.RoundToInt(y1), iy0 + 1, H);
            return new RectInt(ix0, iy0, ix1 - ix0, iy1 - iy0);
        }

        static Color32[] Cut(Texture2D full, RectInt b)
        {
            var px = full.GetPixels32(); var crop = new Color32[b.width * b.height];
            for (int y = 0; y < b.height; y++) Array.Copy(px, (b.y + y) * full.width + b.x, crop, y * b.width, b.width);
            return crop;
        }

        static void Crops(Shot s)
        {
            var hud = OpenZHud.I; var cam = hud.rts.GetComponent<Camera>();
            var full = Load(shotPath); if (full == null) return;
            float sf = Screen.height / 1080f * OzText.UiScale;
            index.Append("- `").Append(Path.GetFileName(shotPath)).Append("`");
            var beforePath = Path.Combine(dir, $"{s.name}_before_live.png");
            Texture2D before = phase == "after" && s.state == "live" && File.Exists(beforePath) ? Load(beforePath) : null;
            foreach (var o in hud.W.Objects)
            {
                if (!o.IsProducer) continue;
                var sp = cam.WorldToScreenPoint(hud.game.CenterWorld(o) + Vector3.up * 7f);
                if (sp.z < 0 || sp.x < -40f * sf || sp.y < -40f * sf || sp.x > Screen.width + 40f * sf || sp.y > Screen.height + 40f * sf) continue;
                if (o.Owner == Team.Neutral && !rects.ContainsKey(o.Id) && phase == "after") continue;
                var b = Box(o, cam, sf, full.width, full.height);
                string role = "b" + o.Id; foreach (var r in roles) if (r.b == o) role = r.name;
                string baseName = $"{s.name}_{phase}_{s.state}_{role}";
                var crop = Cut(full, b);
                Save(Path.Combine(dir, "crops", baseName + "_1x.png"), crop, b.width, b.height, 1);
                var z3 = Path.Combine(dir, "crops", baseName + "_3x.png");
                Save(z3, crop, b.width, b.height, 3);
                index.Append(" ").Append(role).Append(" TECH ").Append(o.BuildLevel + 1).Append(o.Building && o.CurUnit != null ? " " + o.CurUnit : "");
                if (before != null && before.width == full.width && before.height == full.height)
                {   // the before frame cut to the SAME box: the old and the new badge on the same pixels
                    var bc = Cut(before, b);
                    var b3 = Path.Combine(dir, "crops", $"{s.name}_before_live_{role}_samebox_3x.png");
                    Save(b3, bc, b.width, b.height, 3);
                    SideBySide(b3, z3, Path.Combine(dir, "side_by_side", $"{s.name}_{role}_before_after_3x.png"));
                }
            }
            index.AppendLine();
            if (before != null) UnityEngine.Object.DestroyImmediate(before);
            UnityEngine.Object.DestroyImmediate(full);
        }

        static void Save(string path, Color32[] c, int w, int h, int z)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var t = new Texture2D(w * z, h * z, TextureFormat.RGBA32, false);
            if (z == 1) t.SetPixels32(c);
            else
            {
                var o = new Color32[w * z * h * z];
                for (int y = 0; y < h * z; y++) for (int x = 0; x < w * z; x++) o[y * w * z + x] = c[(y / z) * w + x / z];
                t.SetPixels32(o);
            }
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }

        static void SideBySide(string a, string b, string outPath)
        {
            var ta = Load(a); var tb = Load(b); if (ta == null || tb == null) return;
            int gap = 12, w = ta.width + gap + tb.width, h = Mathf.Max(ta.height, tb.height);
            var o = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var bg = new Color32[w * h]; for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(11, 13, 12, 255);
            o.SetPixels32(bg);
            o.SetPixels32(0, h - ta.height, ta.width, ta.height, ta.GetPixels32());
            o.SetPixels32(ta.width + gap, h - tb.height, tb.width, tb.height, tb.GetPixels32());
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, o.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(ta); UnityEngine.Object.DestroyImmediate(tb); UnityEngine.Object.DestroyImmediate(o);
        }

        static void Finish(string err)
        {
            EditorApplication.update -= Tick;
            shooting = false;
            SetForce("live");
            var hud = OpenZHud.I;
            if (Application.isPlaying && hud)
            {
                if (hud.rts) { hud.rts.edgePx = edgePx; hud.rts.focus = focus0; hud.rts.SetHeightImmediate(height0); }
                var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); if (dn && tod0 >= 0f) { dn.timeOfDay = tod0; dn.running = dnRunning; }
                if (hud.Production && hud.Production.IsOpen) hud.Production.Close();
                hud.input.Selection.Clear(); hud.input.Selection.AddRange(sel0);
            }
            try { File.WriteAllText(Path.Combine(dir, "index_" + phase + ".md"), index.ToString()); } catch (Exception) { }
            if (err != null) Debug.LogError("[BadgeProof] " + err);
            else Debug.Log("[BadgeProof] " + phase + " done: " + shots.Count + " shots -> " + dir);
        }
    }
}
#endif
