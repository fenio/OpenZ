using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Play-mode check of DV81 "Ctrl to attack an empty vehicle or gun" through the game's own input code. Step 1 puts 40 pairs on
    /// the map round the point the camera looks at (a unit of the player and an empty vehicle or gun 4 tiles east of it; 20 pairs
    /// for a plain right-click, 20 for a right-click with Ctrl), asks PlayerInput.BuildWaypoint for the order of each click exactly
    /// as a right-click on the target would, counts the orders, then sends them. Step 2, half a minute later, counts what happened
    /// to the targets. Editor only; nothing of it is in a build.
    /// Menus: OpenZ/Proof/Ctrl Attack Empty 1 Orders, OpenZ/Proof/Ctrl Attack Empty 2 Results.
    /// </summary>
    public static class OpenZCtrlAttackEmptyProof
    {
        static readonly string[] Units = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "electrobot", "jeep", "light", "medium", "heavy", "missile_launcher", "gatling", "gun", "howitzer", "missile_cannon" };
        static readonly string[] Targets = { "jeep", "gatling", "light", "gun", "medium", "howitzer", "heavy", "missile_cannon", "apc", "missile_launcher", "crane" };
        const int N = 20;

        sealed class Pair { public ZObject unit, target; public bool ctrl, robot; public ZWaypoint wp; public int hp0; public Team team; public string name; }
        static readonly List<Pair> pairs = new List<Pair>();

        static bool IsGun(string t) => t == "gatling" || t == "gun" || t == "howitzer" || t == "missile_cannon";
        static bool IsRobot(string t) => t == "grunt" || t == "psycho" || t == "sniper" || t == "tough" || t == "pyro" || t == "laser" || t == "electrobot";

        static ZObject Spawn(ZGame g, MethodInfo free, string cat, string type, Team team, int tx, int ty)
        {
            var w = g.World;
            if (free != null)
            {
                var a = new object[] { w, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", 0, 0 };
                if ((bool)free.Invoke(null, a)) { tx = (int)a[5]; ty = (int)a[6]; }
            }
            ZObject o;
            if (cat == "robot")
            {
                o = w.CreateRobotGroup(type, tx * 16, ty * 16, team);
                g.SpawnUnitView(o);
                foreach (var m in o.Minions) g.SpawnUnitView(m);
            }
            else
            {
                o = w.CreateObject(cat, type, tx * 16, ty * 16, team);
                g.SpawnUnitView(o);
            }
            return o;
        }

        [MenuItem("OpenZ/Proof/Ctrl Attack Empty 1 Orders")]
        static void OrdersMenu() { Debug.Log(Orders()); }

        /// <summary>Step 1. Returns the report (first word PASS or FAIL).</summary>
        public static string Orders()
        {
            const string tag = "[CtrlAttackEmpty] orders: ";
            if (!Application.isPlaying) return "FAIL " + tag + "enter Play mode and start a level first";
            var g = ZGame.I;
            if (g == null || g.World == null) return "FAIL " + tag + "no level is running";
            var input = Object.FindFirstObjectByType<PlayerInput>();
            if (input == null) return "FAIL " + tag + "no PlayerInput in the scene";
            var build = typeof(PlayerInput).GetMethod("BuildWaypoint", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (build == null) return "FAIL " + tag + "PlayerInput.BuildWaypoint not found";
            var free = typeof(OzCheatSpawner).GetMethod("FreeTile", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var cam = Camera.main;
            if (cam == null) return "FAIL " + tag + "no camera";
            if (!g.GroundPoint(cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)), out var centre)) return "FAIL " + tag + "no ground under the screen centre";
            var px = ZGame.WorldToPx(centre);
            int fx = px.x >> 4, fy = px.y >> 4;
            Team me = g.player;
            pairs.Clear();
            for (int i = 0; i < 2 * N; i++)
            {
                bool ctrl = i >= N; int k = i % N;
                string ut = Units[k % Units.Length], tt = Targets[k % Targets.Length];
                int tx = fx - 18 + (k % 5) * 8, ty = fy - 14 + (k / 5 + (ctrl ? 4 : 0)) * 4;
                var u = Spawn(g, free, IsRobot(ut) ? "robot" : IsGun(ut) ? "cannon" : "vehicle", ut, me, tx, ty);
                var h = Spawn(g, free, IsGun(tt) ? "cannon" : "vehicle", tt, Team.Neutral, tx + 4, ty);
                if (ctrl || !IsRobot(ut)) h.NoAutoEnterUntilTick = long.MaxValue;                     // only an order gets into these (the pairs stand close together)
                var wp = (ZWaypoint)build.Invoke(input, new object[] { u, h, h.CenterX, h.CenterY, ctrl, false });
                pairs.Add(new Pair { unit = u, target = h, ctrl = ctrl, robot = IsRobot(ut), wp = wp, hp0 = h.Health, team = me, name = ut + " -> empty " + tt });
            }
            int plainAttack = 0, plainEnter = 0, plainMove = 0, ctrlAttack = 0, robots = 0, others = 0;
            var bad = new StringBuilder();
            foreach (var p in pairs)
            {
                bool right;
                if (p.ctrl) { right = p.wp.mode == WpMode.Attack && p.wp.refId == p.target.Id; if (right) ctrlAttack++; }
                else
                {
                    if (p.wp.mode == WpMode.Attack) plainAttack++;
                    if (p.robot) { robots++; right = p.wp.mode == WpMode.Enter && p.wp.refId == p.target.Id; if (right) plainEnter++; }
                    else { others++; right = p.wp.mode == WpMode.Move && p.wp.refId == -1; if (right) plainMove++; }
                }
                if (!right) bad.Append("\n  wrong: ").Append(p.ctrl ? "Ctrl + " : "").Append(p.name).Append(" gave ").Append(p.wp.mode).Append(" ref ").Append(p.wp.refId);
            }
            foreach (var p in pairs) g.World.SendWaypoints(p.team, p.unit, new List<ZWaypoint> { p.wp });
            bool pass = plainAttack == 0 && plainEnter == robots && plainMove == others && ctrlAttack == N;
            return (pass ? "PASS " : "FAIL ") + tag + N + " right-clicks without Ctrl: " + plainAttack + " attack orders, " + plainEnter + " of " + robots + " robot squads got ENTER, "
                   + plainMove + " of " + others + " vehicles and guns got a plain move; " + N + " right-clicks with Ctrl: " + ctrlAttack + " attack orders. All " + (2 * N)
                   + " orders are sent now: watch them, then run step 2 after 30 s of game time." + bad;
        }

        [MenuItem("OpenZ/Proof/Ctrl Attack Empty 2 Results")]
        static void ResultsMenu() { Debug.Log(Results()); }

        /// <summary>Step 2, 30 s of game time after step 1. Returns the report (first word PASS or FAIL).</summary>
        public static string Results()
        {
            const string tag = "[CtrlAttackEmpty] results: ";
            if (!Application.isPlaying || pairs.Count == 0) return "FAIL " + tag + "run step 1 first, in the same Play session";
            int ctrlDamaged = 0, plainHurt = 0, robots = 0, entered = 0;
            var bad = new StringBuilder();
            foreach (var p in pairs)
            {
                var h = p.target;
                if (p.ctrl)
                {
                    if (h.Destroyed || h.Health < p.hp0) ctrlDamaged++;
                    else bad.Append("\n  not damaged: Ctrl + ").Append(p.name);
                }
                else
                {
                    if (h.Health < p.hp0) { plainHurt++; bad.Append("\n  HURT without Ctrl: ").Append(p.name); }
                    if (p.robot)
                    {
                        robots++;
                        if (h.Owner == p.team && h.Drivers.Count > 0) entered++;
                        else bad.Append("\n  not entered: ").Append(p.name);
                    }
                }
            }
            bool pass = plainHurt == 0 && ctrlDamaged == N && entered == robots;
            return (pass ? "PASS " : "FAIL ") + tag + "without Ctrl " + plainHurt + " of " + N + " empty targets lost health, " + entered + " of " + robots + " were entered by the robots sent; with Ctrl "
                   + ctrlDamaged + " of " + N + " targets damaged or destroyed." + bad;
        }
    }
}
