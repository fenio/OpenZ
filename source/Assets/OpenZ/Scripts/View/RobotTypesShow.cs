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
    /// Robot types show (menus OpenZ > Robots v3: 4 (day) / 5 (night), Play mode): each robot type on its own, near the camera: grunt, psycho, tough, sniper, pyro, laser in
    /// turn, SegmentSeconds (15 s) each. For every type a RED squad is spawned on open ground next to the camera focus, facing a BLUE
    /// grunt squad (killable, harmless: it never shoots back) and a BLUE light tank (unkillable, harmless); the squad is ordered onto the
    /// blue robots first and onto the tank at half time, so every type is seen attacking robots AND a vehicle. The next type replaces
    /// the stage (the old units leave like a vehicle entering a repair bay: removed from the sim, not killed). The CPU bots are paused
    /// for the show and put back after it (they would order the blue targets away), the clock is held at day (0.08) or night (0.62)
    /// Test harness only: it writes sim state the way OpenZPyroFireShow / WeaponsV2Check do (spawns, orders, passive targets); the
    /// rules, damage, ranges and timings of the fighting units are untouched.
    /// </summary>
    public sealed class RobotTypesShow : MonoBehaviour
    {
        public static string[] Order = { "grunt", "psycho", "tough", "sniper", "pyro", "laser" };   // settable: harnesses may run a subset (RobotsV3DetailProof)
        /// <summary>Seconds each type fights (about 15 s each).</summary>
        public static float SegmentSeconds = 15f;
        /// <summary>Camera height during the show (m; the default game view is 70 m, the close tactical view 22 m).</summary>
        public static float CameraHeight = 42f;
        /// <summary></summary>
        public static bool Overlay = true;
        /// <summary>Pause the CPU bots during the show (they would send the blue targets away or refuse red orders in bot-vs-bot).</summary>
        public static bool PauseBots = true;

        static RobotTypesShow running;
        public static bool Running => running;

        public static RobotTypesShow Begin(bool night)
        {
            if (!ZGame.I || ZGame.I.World == null) { Debug.LogWarning("[RobotTypesShow] no running ZGame (enter Play mode on L02 or L17)"); return null; }
            if (running) { Debug.LogWarning("[RobotTypesShow] already running"); return running; }
            var s = ZGame.I.gameObject.AddComponent<RobotTypesShow>();
            s.night = night;
            return s;
        }

        static readonly Dictionary<string, string> Watch = new Dictionary<string, string>
        {
            ["grunt"] = "slim BS-01 with its rifle: one orange legacy BOLT from the muzzle + the bolt sound, about one per second",
            ["psycho"] = "FAT slab body, crested helmet, gatling at the belly: machine-gun BURSTS (many tracers, brass casings, MG sound), the body shakes only while it fires",
            ["tough"] = "FATTEST egg body + pot belly: ~0.8 s prepare (step, brace, tube down) before the 1st rocket; rocket out of the tube FRONT on his RIGHT shoulder, blast out the rear; upper body kicked back + step back",
            ["sniper"] = "slim, bush hat, long scoped rifle: SOMETIMES down on one knee or prone; laser sight + glint, then a crack, a long tracer and an echo; slow aimed shots at range",
            ["pyro"] = "round belly, twin tanks: legacy flame stream, glowing bloom fire, light on the ground, flame roar; sticky fire on what it hits + burning ground",
            ["laser"] = "heavy armour, arm cannon, green glows: moving green DASHES (not a line) that bloom by day; green flash + sparks + zap on every hit",
        };

        ZGame g; ZWorld W; bool night;
        string root, stamp;
        readonly List<ZObject> stage = new List<ZObject>();
        readonly List<ZObject> red = new List<ZObject>(), blueRobots = new List<ZObject>();
        readonly StringBuilder log = new StringBuilder(), rows = new StringBuilder();
        int pass, check;
        string title = "", watch = ""; float segEnd;
        GUIStyle big, small;

        void Start() { running = this; StartCoroutine(Run()); }
        void OnDestroy() { if (running == this) running = null; }

        // ------------------------------------------------------------------------------------------------ stage helpers
        (int, int) Spot(int tx, int ty, bool robot)
        {
            for (int r = 0; r < 8; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = tx + dx, y = ty + dy;
                        bool ok = robot ? W.Path.TilePassable(x, y, true)
                                        : W.Path.TilePassable(x, y, false) && W.Path.TilePassable(x + 1, y, false) && W.Path.TilePassable(x, y + 1, false) && W.Path.TilePassable(x + 1, y + 1, false);
                        if (ok) return (x, y);
                    }
            return (tx, ty);
        }

        /// <summary>Open ground near the camera focus: a 17 x 7 tile field every robot can walk (85 % of it vehicle ground too, so
        /// no water / rock field) with no red or blue unit / building within 12 tiles (forts, guns and patrols stay out of the show).</summary>
        (int, int, bool) Field(int fx, int fy)
        {
            for (int r = 0; r <= 40; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int cx = fx + dx, cy = fy + dy;
                        if (FieldOk(cx, cy)) return (cx, cy, true);
                    }
            return (fx, fy, false);
        }

        bool FieldOk(int cx, int cy)
        {
            int veh = 0, all = 0;
            for (int x = cx - 8; x <= cx + 8; x++)
                for (int y = cy - 3; y <= cy + 3; y++)
                {
                    if (!W.Path.TilePassable(x, y, true)) return false;
                    all++; if (W.Path.TilePassable(x, y, false)) veh++;
                }
            if (veh < all * 0.85f) return false;
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == Team.Neutral) continue;
                if (Mathf.Abs(o.CenterX / 16 - cx) <= 12 && Mathf.Abs(o.CenterY / 16 - cy) <= 12) return false;
            }
            return true;
        }

        IEnumerable<ZObject> Members(ZObject leader)
        {
            if (leader == null) yield break;
            yield return leader;
            foreach (var m in leader.Minions) if (m != null) yield return m;
        }

        ZObject Squad(string type, int tx, int ty, Team t)
        {
            var (x, y) = Spot(tx, ty, true);
            var o = W.CreateRobotGroup(type, x * 16, y * 16, t);
            if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            foreach (var m in Members(o)) stage.Add(m);
            return o;
        }

        ZObject Vehicle(string type, int tx, int ty, Team t)
        {
            var (x, y) = Spot(tx, ty, false);
            var o = W.CreateObject("vehicle", type, x * 16, y * 16, t);
            if (o == null) return null;
            g.SpawnUnitView(o); stage.Add(o);
            return o;
        }

        static void Passive(ZObject o, bool unkillable)
        {
            if (o == null) return;
            foreach (var m in new List<ZObject>(ZObjectAndMinions(o)))
            {
                m.AttackRadius = 0; m.Damage = 0;                                                     // never shoots back
                if (unkillable) m.MaxHealth = m.Health = 1000000000;
            }
        }
        static IEnumerable<ZObject> ZObjectAndMinions(ZObject o) { yield return o; foreach (var m in o.Minions) if (m != null) yield return m; }

        bool Alive(ZObject o) => o != null && !o.Destroyed && W.Get(o.Id) != null;

        ZObject Lead(ZObject o)
        {   // the squad's current leader (a dead leader hands over to a minion: ZWorld.RemoveObjectFromGroup)
            for (int i = 0; i < 8 && o != null && !Alive(o); i++) o = o.Successor;
            if (o != null && o.IsMinion) o = o.Leader;
            return Alive(o) ? o : null;
        }

        bool Attack(ZObject squad, ZObject target, string what)
        {
            var u = Lead(squad);
            if (u == null || !Alive(target)) return false;
            var wp = new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY, attackTo = true };
            bool ok = W.SendWaypoints(u.Owner, u, new[] { wp });
            log.AppendLine($"- {Time.unscaledTime:F1} s: {u.Type} squad ordered onto {what} -> {(ok ? "ok" : "refused")}");
            return ok;
        }

        ZObject AnyBlueRobot() { foreach (var o in blueRobots) if (Alive(o)) return o; return null; }

        /// <summary>The previous type's units leave the stage like a vehicle entering a repair bay (ZWorld.UnitEnterRepairBuilding:
        /// DoKillMe(0), gone, not dead) and their views hide; corpses already on the ground stay until they pop out.</summary>
        void ClearStage()
        {
            foreach (var o in stage)
            {
                if (o == null) continue;
                bool alive = W.Get(o.Id) != null && !o.Destroyed;
                if (alive) o.KillAt = W.Time;
                if (alive && g.Views.TryGetValue(o.Id, out var v) && v) v.Hide();
            }
            stage.Clear(); red.Clear(); blueRobots.Clear();
        }

        void Shot(string name)
        {
            try
            {
                var dir = Path.Combine(root, "work", "records", "milestones"); Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"robot_types_show_{g.level}_{stamp}_{name}.png");
                ScreenCapture.CaptureScreenshot(path);
                log.AppendLine($"- {Time.unscaledTime:F1} s: shot {path}");
            }
            catch (System.Exception ex) { log.AppendLine("- shot failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------------------------------------ pyro_fire_v2 (by name: no compile dependency)
        static object PyroI()
        {
            var t = System.Type.GetType("OpenZ.OpenZPyroFire");
            var p = t?.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            return p?.GetValue(null);
        }
        static int PyroInt(object inst, string prop)
        {
            if (inst == null) return -1;
            try { var p = inst.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance); return p != null && p.GetValue(inst) is int v ? v : -1; }
            catch (System.Exception) { return -1; }
        }

        static int[] Snap(WeaponsV2Runner r) => r == null ? new int[11] : new[] { r.GruntShots, r.MgBursts, r.MgRounds, r.Casings, r.LaserShots, r.LaserBolts, r.Zaps,
            r.SniperShots, r.SniperSkipped, r.SniperKillShots, r.Launches };

        // ------------------------------------------------------------------------------------------------ the show
        IEnumerator Run()
        {
            g = ZGame.I; W = g.World;
            root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            stamp = System.DateTime.Now.ToString("HHmm");
            var cam = FindFirstObjectByType<RtsCamera>();
            var focus = cam ? cam.focus : Vector3.zero;
            int fx = Mathf.RoundToInt(focus.x * 8f / 16f), fy = Mathf.RoundToInt(-focus.z * 8f / 16f);
            var (cx, cy, open) = Field(fx, fy);
            log.AppendLine($"camera focus tile ({fx},{fy}); stage centre tile ({cx},{cy}){(open ? "" : " (NO open field within 40 tiles: staged at the focus, other units may join in)")}");

            // hold the clock, pause the bots
            var dn = FindFirstObjectByType<DayNight>(); float tod = dn ? dn.timeOfDay : 0.08f; bool dnRunning = dn && dn.running;
            if (dn) { dn.running = false; NightLights.JumpTo(night ? 0.62f : 0.08f); }
            var bots = new List<ZBot>(W.Bots);
            if (PauseBots) W.Bots.Clear();
            var au = CombatAudio.I;
            var run = WeaponsV2Runner.Ensure();
            if (cam) { cam.FlyTo(ZGame.PxToWorld(cx * 16 + 8, cy * 16 + 8)); cam.ZoomTo(CameraHeight); }

            // the sim must run (the level-start briefing pauses it)
            double simT0 = W.Time; float realT0 = Time.unscaledTime;
            while (W.Time <= simT0 && Time.unscaledTime - realT0 < 4f) yield return null;
            bool simRuns = W.Time > simT0;
            if (!simRuns) log.AppendLine("- the sim clock did not move in 4 s (level-start briefing open / game paused?): show aborted, close it and run the show again");

            for (int i = 0; simRuns && i < Order.Length; i++)
            {
                string type = Order[i];
                ClearStage();
                // distance to the targets: a fraction of the type's own Zod attack radius (sniper: a long shot, so it lies down)
                var squad = Squad(type, cx - 5, cy, Team.Red);
                if (squad == null) { rows.AppendLine($"| {i + 1} | {type} | - | - | - | - | could not spawn | **CHECK** |"); check++; continue; }
                foreach (var m in Members(squad)) red.Add(m);
                float frac = type == "sniper" ? 0.8f : type == "pyro" ? 0.55f : 0.6f;
                int d = Mathf.Clamp(Mathf.RoundToInt(frac * Mathf.Max(16, squad.AttackRadius) / 16f), 3, 10);
                int bx = cx - 5 + d;
                var blue = Squad("grunt", bx, cy - 1, Team.Blue); Passive(blue, false);
                foreach (var m in Members(blue)) blueRobots.Add(m);
                var tank = Vehicle("light", bx + 1, cy + 2, Team.Blue); Passive(tank, true);
                if (cam) cam.FlyTo(ZGame.PxToWorld((cx - 5 + bx) * 8 + 8, cy * 16 + 8));
                title = $"{i + 1}/{Order.Length}  {type.ToUpperInvariant()}  vs blue robots, then a blue tank";
                watch = Watch[type];
                Debug.Log($"[RobotTypesShow] {title}: {watch}");
                log.AppendLine($"## {i + 1}. {type}: red squad of {red.Count} at tile ({cx - 5},{cy}), attack radius {squad.AttackRadius} px, targets {d} tiles away");

                // metrics
                var r0 = Snap(run); var p0 = au ? new Dictionary<string, int>(au.PlayCounts) : new Dictionary<string, int>();
                var pyro = PyroI(); int pyShots0 = PyroInt(pyro, "Shots"), pyLoops0 = PyroInt(pyro, "LoopsStarted"), pyTouch0 = PyroInt(pyro, "TouchIgnites");
                int maxJets = 0, maxBurning = 0, maxPatches = 0, maxLights = 0;
                var engagedAt = new Dictionary<int, float>();
                var poses = new Dictionary<int, string>();
                bool v3 = false, stanceSeen = false;
                foreach (var m in red) if (g.Views.TryGetValue(m.Id, out var mv) && mv && mv.GetComponent<RobotV3>()) v3 = true;
                float t0 = Time.unscaledTime, t0g = Time.time; segEnd = t0 + SegmentSeconds + 1f;
                bool orderedRobots = false, orderedTank = false, shotA = false, shotB = false;
                while (Time.unscaledTime < segEnd)
                {
                    float t = Time.unscaledTime - t0;
                    if (!orderedRobots && t >= 1f) { orderedRobots = true; var tg = Lead(blue) ?? AnyBlueRobot(); if (tg != null) Attack(squad, tg, "the blue robots"); }
                    if (!orderedTank && t >= 1f + SegmentSeconds * 0.5f)
                    {
                        orderedTank = true;
                        if (AnyBlueRobot() == null)
                        {   // all blue robots down: a fresh blue squad, so robots stay on the menu after the tank (or its sniped driver)
                            var more = Squad("grunt", bx, cy - 2, Team.Blue); Passive(more, false); foreach (var m in Members(more)) blueRobots.Add(m);
                            log.AppendLine($"- {Time.unscaledTime:F1} s: the blue robots were all down: a fresh blue squad");
                        }
                        if (Alive(tank) && tank.Owner == Team.Blue) Attack(squad, tank, "the blue tank");
                    }
                    if (!shotA && t >= 5f) { shotA = true; Shot($"{i + 1}_{type}_a"); }
                    if (!shotB && t >= 12f) { shotB = true; Shot($"{i + 1}_{type}_b"); }
                    foreach (var m in red)
                    {
                        if (!Alive(m)) continue;
                        if (m.AttackObject != null && !engagedAt.ContainsKey(m.Id)) engagedAt[m.Id] = Time.time;
                        if (g.Views.TryGetValue(m.Id, out var v) && v && v.TryGetComponent(out BS01Stance st) && st.Busy)
                        {
                            stanceSeen = true;
                            if (!string.IsNullOrEmpty(st.Pose)) poses[m.Id] = st.Pose;
                        }
                    }
                    pyro ??= PyroI();
                    if (pyro != null)
                    {
                        maxJets = Mathf.Max(maxJets, PyroInt(pyro, "Jets")); maxBurning = Mathf.Max(maxBurning, PyroInt(pyro, "Burning"));
                        maxPatches = Mathf.Max(maxPatches, PyroInt(pyro, "Patches")); maxLights = Mathf.Max(maxLights, PyroInt(pyro, "LightsOn"));
                    }
                    yield return null;
                }

                // this type's row
                var dd = Snap(run); for (int k = 0; k < dd.Length; k++) dd[k] -= r0[k];
                int Plays(string key) => (au && au.PlayCounts.TryGetValue(key, out var n) ? n : 0) - (p0.TryGetValue(key, out var n0) ? n0 : 0);
                int kills = 0; foreach (var b in blueRobots) if (b.Destroyed) kills++;
                float firstEngage = float.MaxValue; foreach (var kv in engagedAt) firstEngage = Mathf.Min(firstEngage, kv.Value - t0g);
                string engaged = engagedAt.Count > 0 ? $"{firstEngage:F1} s" : "never";
                string shots, sounds, extra = $"{kills} blue robots killed"; bool ok;
                switch (type)
                {
                    case "grunt":
                        shots = $"{dd[0]} bolts"; sounds = $"bolt {Plays("bolt")}"; ok = dd[0] > 0 && Plays("bolt") > 0; break;
                    case "psycho":
                        shots = $"{dd[1]} bursts, {dd[2]} tracer rounds, {dd[3]} casings"; sounds = $"mg {Plays("mg")}";
                        extra += stanceSeen ? ", psycho_fire / aim stance played" : ", NO psycho stance seen";
                        ok = dd[1] > 0 && dd[2] >= dd[1] * 5 && Plays("mg") > 0; break;
                    case "tough":
                    {
                        shots = $"{dd[10]} rockets"; sounds = $"rocket {Plays("rocket")}, backblast {Plays("backblast")}, servo {Plays("servo")}";
                        var first = new List<string>();
                        foreach (var m in red)
                            if (run != null && engagedAt.TryGetValue(m.Id, out var ea) && run.FirstLaunchAt.TryGetValue(m.Id, out var fl) && fl >= ea) first.Add($"{(fl - ea) * 1000f:F0}");
                        extra += $", first rocket after the engage (ms): {(first.Count > 0 ? string.Join(" ", first) : "-")} (DV11 {(ZWorld.ToughPrepareDeviation ? "on: ~800" : "off")})";
                        extra += stanceSeen ? ", tough_prepare / fire played" : ", NO tough stance seen";
                        ok = dd[10] > 0 && Plays("rocket") > 0 && Plays("backblast") > 0; break;
                    }
                    case "sniper":
                    {
                        int kn = 0, pr = 0; foreach (var kv in poses) { if (kv.Value == "kneel") kn++; else if (kv.Value == "prone") pr++; }
                        shots = $"{dd[7]} aimed shots ({dd[9]} kill shots, {dd[8]} rolls shown as aim only)"; sounds = $"sniper {Plays("sniper")}, echo {Plays("sniper_tail")}";
                        extra += $", down: {kn} kneel, {pr} prone, {red.Count - kn - pr} standing";
                        ok = dd[7] > 0 && Plays("sniper") > 0; break;
                    }
                    case "pyro":
                    {
                        int pyShots = pyro != null ? PyroInt(pyro, "Shots") - pyShots0 : -1, pyLoops = pyro != null ? PyroInt(pyro, "LoopsStarted") - pyLoops0 : -1;
                        int touch = pyro != null ? PyroInt(pyro, "TouchIgnites") - pyTouch0 : -1;
                        shots = pyro != null ? $"{pyShots} flame shots, max {maxJets} jets" : "OpenZPyroFire not in the project (pyro_fire_v2 not applied)";
                        sounds = pyro != null ? $"{pyLoops} flame loops" : "-";
                        extra += pyro != null ? $", max {maxBurning} burning things, {maxPatches} ground fires, {maxLights} fire lights, {touch} props lit on the way" : "";
                        ok = pyro != null && pyShots > 0 && pyLoops > 0 && maxJets > 0 && maxLights > 0; break;
                    }
                    default:
                        shots = $"{dd[4]} shots, {dd[5]} moving bolts, {dd[6]} zaps"; sounds = $"laser {Plays("laser")}, laser_hit {Plays("laser_hit")}";
                        ok = dd[4] > 0 && dd[5] >= dd[4] && dd[6] > 0 && Plays("laser") > 0 && Plays("laser_hit") > 0; break;
                }
                bool modelOk = type == "grunt" || v3;
                string model = type == "grunt" ? "BS-01 (unchanged)" : v3 ? "v3 (robots_v3)" : "**OLD model (run Robots v3: 1)**";
                bool rowOk = ok && modelOk && engagedAt.Count > 0;
                if (rowOk) pass++; else check++;
                rows.AppendLine($"| {i + 1} | {type} | {model} | {engaged} | {shots} | {sounds} | {extra} | {(rowOk ? "PASS" : "**CHECK**")} |");
            }

            // put everything back
            ClearStage();
            title = watch = "";
            if (PauseBots) foreach (var b in bots) if (!W.Bots.Contains(b)) W.Bots.Add(b);
            if (dn) { dn.timeOfDay = tod; dn.Apply(tod); dn.running = dnRunning; }

            var sb = new StringBuilder();
            sb.AppendLine($"# Robot types show, {g.level}, {System.DateTime.Now:yyyy-MM-dd HH:mm}, {(night ? "night (0.62)" : "day (0.08)")}");
            sb.AppendLine();
            sb.AppendLine($"RESULT: {(check == 0 && simRuns ? "PASS" : "CHECK")} ({pass} pass, {check} to check). Each type {SegmentSeconds:F0} s: a red squad vs a harmless blue grunt squad, " +
                          $"then a harmless unkillable blue light tank; CPU bots paused; game speed {g.gameSpeed:F2}; Tough prepare DV11 {(ZWorld.ToughPrepareDeviation ? "on" : "off")}. " +
                          "The numbers say the systems fired; eyes and ears decide the look (the overlay text is the checklist).");
            sb.AppendLine();
            sb.AppendLine("| # | Type | Model | Engaged after | Drawn | Sounds (CombatAudio plays) | Details | Result |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            sb.Append(rows);
            sb.AppendLine();
            sb.AppendLine("## Timeline").Append(log);
            var qc = Path.Combine(root, "work", "records", "qc");
            string text = sb.ToString();
            try { Directory.CreateDirectory(qc); File.WriteAllText(Path.Combine(qc, $"robot_types_show_{g.level}_{stamp}.md"), text); }
            catch (System.Exception ex) { Debug.LogWarning("[RobotTypesShow] report not written: " + ex.Message); }
            Debug.Log($"[RobotTypesShow] {(check == 0 && simRuns ? "PASS" : "CHECK")} {pass}/{Order.Length} -> work/records/qc/robot_types_show_{g.level}_{stamp}.md\n" + text);
            Destroy(this);
        }

        void OnGUI()
        {
            if (!Overlay || string.IsNullOrEmpty(title)) return;
            float k = Mathf.Max(0.75f, Screen.height / 1080f);
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            }
            big.fontSize = Mathf.RoundToInt(24 * k); small.fontSize = Mathf.RoundToInt(16 * k);
            big.normal.textColor = new Color(1f, 0.82f, 0.45f); small.normal.textColor = Color.white;
            float w = Mathf.Min(Screen.width * 0.7f, 1300f * k), h = 92f * k;
            var r = new Rect((Screen.width - w) * 0.5f, 14f * k, w, h);
            var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = prev;
            GUI.Label(new Rect(r.x + 10f, r.y + 6f * k, r.width - 20f, 34f * k), $"{title}   {Mathf.Max(0f, segEnd - Time.unscaledTime):0} s", big);
            GUI.Label(new Rect(r.x + 14f, r.y + 40f * k, r.width - 28f, h - 42f * k), watch, small);
        }
    }
}
