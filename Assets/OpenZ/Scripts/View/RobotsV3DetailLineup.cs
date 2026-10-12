using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Menu "OpenZ/Robots v3 detail: 4 Lineup (Play mode, day)" / "5 ... (night)". On open ground next to the camera focus it stands
    /// one robot of every type in a row, grunt | psycho | tough | sniper | pyro | laser | electrobot (the Electrobot when the sim knows
    /// it), one RED row and one BLUE row, all passive, the CPU bots paused and the day / night clock held; then it takes Game-view
    /// shots at the default RTS height (70 m) and at the close tactical height (22 m), and writes a report of the materials every
    /// Test harness only: it spawns and removes units the way RobotTypesShow does; nothing of the game rules changes.
    /// </summary>
    public class RobotsV3DetailLineup : MonoBehaviour
    {
        public static readonly string[] Order = { "grunt", "psycho", "tough", "sniper", "pyro", "laser", "electrobot" };
        public static float RtsHeight = 70f, CloseHeight = 22f, Settle = 2.5f;
        static RobotsV3DetailLineup running;
        public static bool Running => running;
        ZGame g; ZWorld W; bool night; string root, stamp, banner = "";
        readonly List<ZObject> stage = new List<ZObject>();
        readonly StringBuilder log = new StringBuilder();
        GUIStyle st;

        public static RobotsV3DetailLineup Begin(bool night)
        {
            if (!ZGame.I || ZGame.I.World == null) { Debug.LogWarning("[RobotsV3DetailLineup] no running ZGame (enter Play mode on L02 or L17)"); return null; }
            if (running) return running;
            var s = ZGame.I.gameObject.AddComponent<RobotsV3DetailLineup>(); s.night = night; return s;
        }

        void Start() { running = this; StartCoroutine(Run()); }
        void OnDestroy() { if (running == this) running = null; }

        bool FieldOk(int cx, int cy)
        {
            for (int x = cx - 8; x <= cx + 8; x++)
                for (int y = cy - 3; y <= cy + 3; y++)
                    if (!W.Path.TilePassable(x, y, true)) return false;
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == Team.Neutral) continue;
                if (Mathf.Abs(o.CenterX / 16 - cx) <= 11 && Mathf.Abs(o.CenterY / 16 - cy) <= 11) return false;
            }
            return true;
        }

        (int, int, bool) Field(int fx, int fy)
        {
            for (int r = 0; r <= 40; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        if (FieldOk(fx + dx, fy + dy)) return (fx + dx, fy + dy, true);
                    }
            return (fx, fy, false);
        }

        ZObject One(string type, int tx, int ty, Team t)
        {
            ZObject o = null;
            try { o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); } catch (System.Exception e) { log.AppendLine($"- {type}: cannot spawn ({e.Message})"); }
            if (o == null) return null;
            g.SpawnUnitView(o);
            foreach (var m in o.Minions.ToList())
            {   // one robot per type: the squad's minions leave at once (ClearStage style: gone, not dead)
                if (m == null) continue;
                m.KillAt = W.Time;
            }
            o.AttackRadius = 0; o.Damage = 0; o.MaxHealth = o.Health = 1000000000;
            stage.Add(o);
            return o;
        }

        void Shot(string name)
        {
            var dir = Path.Combine(root, "work", "records", "milestones"); Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, $"robots_v3_detail_lineup_{g.level}_{(night ? "night" : "day")}_{name}_{stamp}.png");
            ScreenCapture.CaptureScreenshot(p);
            log.AppendLine($"- shot `{p}`");
        }

        IEnumerator Run()
        {
            g = ZGame.I; W = g.World;
            root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            stamp = System.DateTime.Now.ToString("HHmm");
            var cam = FindFirstObjectByType<RtsCamera>();
            var focus = cam ? cam.focus : Vector3.zero;
            int fx = Mathf.RoundToInt(focus.x * 8f / 16f), fy = Mathf.RoundToInt(-focus.z * 8f / 16f);
            var (cx, cy, open) = Field(fx, fy);
            log.AppendLine($"# robots_v3_detail lineup, {g.level}, {(night ? "night" : "day")}, {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            log.AppendLine($"- camera focus tile ({fx},{fy}), row centre tile ({cx},{cy}){(open ? "" : " (no open field within 40 tiles: at the focus)")}");
            var dn = FindFirstObjectByType<DayNight>(); float tod = dn ? dn.timeOfDay : 0.08f; bool dnRunning = dn && dn.running;
            if (dn) { dn.running = false; NightLights.JumpTo(night ? 0.62f : 0.08f); }
            var bots = new List<ZBot>(W.Bots); W.Bots.Clear();
            var rows = new StringBuilder();
            for (int i = 0; i < Order.Length; i++)
            {
                int x = cx - 6 + 2 * i;
                var r = One(Order[i], x, cy + 1, Team.Red);
                var b = One(Order[i], x, cy - 1, Team.Blue);
                rows.AppendLine($"| {i + 1} | {Order[i]} | {(r != null ? Mats(r) : "not spawned")} | {(b != null ? Mats(b) : "not spawned")} |");
            }
            banner = "robots_v3_detail  |  grunt  psycho  tough  sniper  pyro  laser  electrobot  |  one red row, one blue row";
            if (cam) { cam.FlyTo(ZGame.PxToWorld(cx * 16 + 8, cy * 16 + 8)); cam.ZoomTo(RtsHeight); }
            yield return new WaitForSecondsRealtime(Settle + 1f);
            Shot("rts");
            if (cam) cam.ZoomTo(CloseHeight);
            yield return new WaitForSecondsRealtime(Settle);
            Shot("close");
            if (cam) cam.FlyTo(ZGame.PxToWorld((cx - 3) * 16 + 8, (cy + 1) * 16 + 8));
            yield return new WaitForSecondsRealtime(Settle);
            Shot("close_left");
            if (cam) cam.FlyTo(ZGame.PxToWorld((cx + 3) * 16 + 8, (cy + 1) * 16 + 8));
            yield return new WaitForSecondsRealtime(Settle);
            Shot("close_right");
            yield return new WaitForSecondsRealtime(0.5f);
            log.AppendLine();
            log.AppendLine("| # | type | red: renderer materials | blue: renderer materials |");
            log.AppendLine("|---|---|---|---|");
            log.Append(rows);
            foreach (var o in stage)
            {
                if (o == null) continue;
                if (W.Get(o.Id) != null && !o.Destroyed) o.KillAt = W.Time;
                if (g.Views.TryGetValue(o.Id, out var v) && v) v.Hide();
            }
            stage.Clear();
            W.Bots.AddRange(bots.Where(bb => !W.Bots.Contains(bb)));
            if (dn) { NightLights.JumpTo(tod); dn.running = dnRunning; }
            banner = "";
            try
            {
                var dir = Path.Combine(root, "work", "records", "qc"); Directory.CreateDirectory(dir);
                var p = Path.Combine(dir, $"robots_v3_detail_lineup_{g.level}_{(night ? "night" : "day")}_{stamp}.md");
                File.WriteAllText(p, log.ToString());
                Debug.Log($"[RobotsV3DetailLineup] done -> {p}\n{log}");
            }
            catch (System.Exception e) { Debug.LogWarning("[RobotsV3DetailLineup] report: " + e.Message); }
            Destroy(this);
        }

        string Mats(ZObject o)
        {
            if (!g.Views.TryGetValue(o.Id, out var v) || !v) return "no view";
            var names = v.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer))
                         .SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct();
            return string.Join(", ", names);
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(banner)) return;
            st ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.45f) } };
            GUI.Label(new Rect(20, 12, Screen.width - 40, 30), banner, st);
        }
    }
}
