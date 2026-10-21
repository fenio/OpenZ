using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Staging helper for the pictures and the clip of an enemy strike group (rule: Sim/ZBot.StrikeGroup.cs). Editor only, Play
    /// mode, in a running level in which the player is red and the computer is blue. Menu: OpenZ/V1.1/Stage Enemy Strike Group
    /// (f39, Play mode), or OpenZ.EditorTools.OzV11StrikeStage.Run().
    ///
    /// A player who does nothing loses his fort before 8:00, so nothing could be seen. The helper therefore gives the player the
    /// stand-in of the counted check (OzV11StrikeCount.StandIn: his four nearest territories each held by a tank and a squad, four
    /// tanks and four squads at his gate, eight units in one large gathering, all on hold position; every 30 s a new unit on every
    /// post of the gate guard and the gathering whose unit is gone), each unit made by the rules and given its view the way the
    /// head start does (ZGame.SpawnUnitView). It changes no rule and no saved setting.
    ///
    /// Then it watches the computer's own record (ZBot.StrikeEvents), writes every entry to the Console and to on_screen_<level>.txt, and
    /// moves the normal game camera (height CamHeight, never the free camera): to the forming ground when a window opens, with the
    /// group while it marches, to the meeting point when the first member arrives. It takes three full-screen pictures into
    /// for_owner: 1 forming (each time one more member stands on its place; the last one stays), 2 on the march (MarchShotSeconds
    /// after the group left), 3 the battle (BattleShotSeconds after the first member reached the target area). With FastForward
    /// the game runs at 400 % until 7:45 and then at the speed it had (for the clip leave the speed at 100 % from there on: the
    /// Console line "WINDOW OPENED" is the moment to start the recording). It ends when the group is released (with Groups = 2: when
    /// the SECOND group is released; its three pictures are named ..._g2), or after MaxMinutes of game time, and puts the camera and
    /// the game speed back. Groups = 2 (menu "..., two groups") keeps the stand-in at strength through the second group, for the
    /// warning check: compare the Console lines "[OpenZ] strike group warning 1:" and "... 2:" with the two FORMED lines.
    /// Written from reading the code; never run in the editor: adjust a wait or a camera height if a picture needs it, and say so.
    /// </summary>
    public static class OzV11StrikeStage
    {
        const string Tag = "[OpenZ strike group staged]";
        public static string Report = "not run";
        /// <summary><level></summary>
        public static string Folder;
        public static bool FastForward = true;
        public static float CamHeight = 70f, MarchShotSeconds = 25f, BattleShotSeconds = 8f, MaxMinutes = 24f;
        /// <summary>How many groups it follows before it ends: 1 (the pictures) or 2 (also the second group: the warning check).</summary>
        public static int Groups = 1;
        static int groupNo, released;
        // the route measure of the march (as the counted check): once a second every member not in a fight and not yet in the target
        // area; off = more than 160 px from the group's route line; a member counts once it has been on the route (a member still on
        // its way from its building when the group left joins it first: its joining time is written apart)
        static double nextRouteLook, worstOff; static int offMembers, joiners; static double worstJoin, marchStart;
        static readonly Dictionary<int, double> offSince = new Dictionary<int, double>(), joinSince = new Dictionary<int, double>();
        static readonly HashSet<int> onRoute = new HashSet<int>(), counted = new HashSet<int>();

        static OzV11StrikeCount.StandInState stand;
        static ZBot bot; static ZWorld world; static RtsCamera rts;
        static int seen, lastId, given; static bool sped, camKept; static float speed0, height0; static Vector3 focus0, aim;
        static double formedAt = -1, arrivedAt = -1; static bool marchShot, battleShot, formingShot, formingTaken; static int tx, ty;
        static string dir, level; static readonly StringBuilder log = new StringBuilder();

        [MenuItem("OpenZ/V1.1/Stage Enemy Strike Group (f39, Play mode)")]
        static void RunOne() { Groups = 1; Run(); }

        [MenuItem("OpenZ/V1.1/Stage Enemy Strike Group, two groups (f39, Play mode)")]
        static void RunTwo() { Groups = 2; if (MaxMinutes < 30f) MaxMinutes = 30f; Run(); }

        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a running level)"); return; }
            if (world != null) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var g = ZGame.I; rts = Object.FindFirstObjectByType<RtsCamera>();
            ZBot b = null;
            if (g && g.World != null) foreach (var x in g.World.Bots) if (x.Team == Team.Blue && !x.Ignored) b = x;
            string no = !g || g.World == null ? "no level is running" : !rts ? "no game camera" : g.paused ? "the game is paused: close the menu first" : g.World.GameOver ? "the match is over"
                      : g.player != Team.Red || b == null || g.World.Bots.Count != 1 ? "the player must be red and the computer blue (no computer match)"
                      : !ZWorld.StrikeGroupDeviation ? "the rule is switched off (OPTIONS, ADVANCED, ENEMY STRIKE GROUPS, or the engine-exact mode): there is nothing to stage"
                      : g.World.Time > 420 ? "start it in the first seven minutes of the match" : null;
            if (no != null) { Report = Tag + " NOT RUN: " + no; Debug.LogError(Report); return; }
            world = g.World; bot = b; level = g.level; seen = bot.StrikeEvents.Count; given = 0; sped = false; groupNo = 0; released = 0; formedAt = arrivedAt = -1; marchShot = battleShot = formingShot = formingTaken = false; log.Clear();
            dir = OutDir(); Directory.CreateDirectory(Path.Combine(dir, "for_owner"));
            lastId = MaxId(); stand = OzV11StrikeCount.StandIn(world); Views(g);
            focus0 = rts.focus; height0 = rts.height; camKept = true; aim = rts.focus;
            speed0 = g.gameSpeed;
            Say($"level {level}, Game view {Screen.width}x{Screen.height}: the stand-in player is placed ({given} robots and vehicles for red); his large gathering stands at px ({stand.gx},{stand.gy}); the next window may open at {Clock(bot.StrikeAllowedAt)}");
            Report = "running";
            EditorApplication.update += Step;
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
            return Path.Combine(all, stage, "f39_strike_groups");
        }

        static string Clock(double t) => ((int)(t / 60)) + ":" + ((int)(t % 60)).ToString("00");
        static Vector3 At(double px, double py) => new Vector3((float)(px / 8.0), 0f, (float)(-py / 8.0));      // rules px -> world m (1 tile = 16 px = 2 m)
        static int MaxId() { int m = 0; foreach (var o in world.Objects) if (o.Id > m) m = o.Id; return m; }
        static void Say(string s) { string line = Clock(world.Time) + " " + s; log.AppendLine(line); Debug.Log(Tag + " " + line); }

        /// <summary>A view for every unit the helper has made since the last call.</summary>
        static void Views(ZGame g)
        {
            foreach (var o in world.Objects) if (o.Id > lastId && o.IsUnit) { g.SpawnUnitView(o); given++; }
            lastId = MaxId();
        }

        static void Shot(string name)
        {
            int gno = bot.StrikePhaseNow == ZBot.StrikePhase.Forming ? groupNo + 1 : groupNo;              // the group the picture shows
            string path = Path.Combine(dir, "for_owner", $"f39_{level}_{name}{(gno > 1 ? "_g" + gno : "")}.png");
            ScreenCapture.CaptureScreenshot(path);
            Say("PICTURE " + path.Replace('\\', '/'));
        }

        static void Step()
        {
            var g = ZGame.I;
            if (!EditorApplication.isPlaying || !g || g.World != world || world == null) { End("the level was left"); return; }
            if (world.GameOver) { End("the match is over"); return; }
            if (world.Time > MaxMinutes * 60) { End("the time limit of the helper"); return; }
            if (g.paused) return;
            lastId = MaxId(); OzV11StrikeCount.StandInKeep(world, stand); Views(g);
            if (FastForward && !sped && world.Time < 465 && bot.StrikePhaseNow == ZBot.StrikePhase.Waiting) { sped = true; g.gameSpeed = 4f; Say("fast forward to 7:45 at 400 %"); }
            if (sped && (world.Time >= 465 || bot.StrikePhaseNow != ZBot.StrikePhase.Waiting)) { sped = false; g.gameSpeed = speed0; Say("game speed back to " + Mathf.RoundToInt(speed0 * 100) + " %"); }
            for (; seen < bot.StrikeEvents.Count; seen++)
            {
                var e = bot.StrikeEvents[seen]; var o = e.unitId >= 0 ? world.GetAny(e.unitId) : null; string who = o != null ? o.Type + " #" + o.Id : "";
                switch (e.kind)
                {
                    case ZBot.StrikeEventKind.WindowOpened:
                    {
                        Say($"WINDOW OPENED (start the recording now): {e.n} feeding buildings; {e.note}");
                        double sx = 0, sy = 0; int k = 0;
                        foreach (var p in bot.StrikePlaces) { if (k >= 6) break; sx += p.x; sy += p.y; k++; }
                        aim = k > 0 ? At(sx / k, sy / k) : At(e.x, e.y);
                        formedAt = arrivedAt = -1; marchShot = battleShot = false;
                        break;
                    }
                    case ZBot.StrikeEventKind.Joined: Say($"{who} joins: its place is px ({e.x},{e.y})"); break;
                    case ZBot.StrikeEventKind.Settled: Say($"{who} stands on its place"); formingShot = true; break;
                    case ZBot.StrikeEventKind.Formed: RouteReset(); marchStart = world.Time; formedAt = world.Time; tx = e.x; ty = e.y; groupNo++; Say($"FORMED, group {groupNo} leaves: {e.n} members for px ({e.x},{e.y}); {e.note} (the warning, when the player owns a radar: the Console line \"[OpenZ] strike group warning {groupNo}:\" within 6 s)"); break;
                    case ZBot.StrikeEventKind.Retargeted: tx = e.x; ty = e.y; Say($"the target moved: px ({e.x},{e.y}), {e.n} units"); break;
                    case ZBot.StrikeEventKind.Arrived: if (arrivedAt < 0) arrivedAt = world.Time; Say($"{who} reaches the target area"); break;
                    case ZBot.StrikeEventKind.MemberLost: Say($"{who} is destroyed"); break;
                    case ZBot.StrikeEventKind.GivenUp: Say($"GIVEN UP: {e.note}; {e.n} members released"); break;
                    case ZBot.StrikeEventKind.Released:
                        if (formedAt >= 0) Say($"ROUTE, group {groupNo}: members more than 160 px (10 tiles) off the group's route for more than 10 s after they were on it: {offMembers} (the longest off {worstOff:0} s); members that were not on the route when the group left (late from their building, or a place to the side) and joined it: {joiners}, the longest until on it {worstJoin:0} s");
                        Say($"RELEASED: {e.note}; {e.n} members act alone again"); released++;
                        if (released >= Groups && (battleShot || formedAt < 0)) { End(Groups > 1 ? "group " + released + " was released" : "the group was released"); return; }
                        break;
                    case ZBot.StrikeEventKind.SingleOrder: Say($"{who} gets its own order"); break;
                    case ZBot.StrikeEventKind.PlaceChanged: Say($"{who}: its place is now px ({e.x},{e.y}), {(e.n == 1 ? "the spot it stands on" : "the next free place")} ({e.note})"); break;
                    case ZBot.StrikeEventKind.MemberLeft: Say($"{who} is released alone ({e.note})"); break;
                }
            }
            if (bot.StrikePhaseNow == ZBot.StrikePhase.Marching && world.Time >= nextRouteLook) { nextRouteLook = world.Time + 1.0; RouteLook(); }
            // the camera: the forming ground, then the group, then the meeting point
            if (bot.StrikePhaseNow == ZBot.StrikePhase.Marching)
            {
                double sx = 0, sy = 0; int k = 0;
                foreach (int id in bot.StrikeMemberIds()) { var o = world.Get(id); if (o != null) { sx += o.CenterX; sy += o.CenterY; k++; } }
                if (k > 0) aim = arrivedAt >= 0 ? At((sx / k + tx) / 2, (sy / k + ty) / 2) : At(sx / k, sy / k);
            }
            if (bot.StrikePhaseNow != ZBot.StrikePhase.Waiting || formedAt >= 0)
            {
                rts.focus = Vector3.Lerp(rts.focus, aim, 0.06f);
                if (Mathf.Abs(rts.height - CamHeight) > 0.01f) rts.SetHeightImmediate(CamHeight);
            }
            if (formingShot && bot.StrikePhaseNow == ZBot.StrikePhase.Forming && (rts.focus - aim).sqrMagnitude < 1f) { formingShot = false; formingTaken = true; Shot("1_forming"); }
            if (formedAt >= 0 && !marchShot && arrivedAt < 0 && world.Time - formedAt >= MarchShotSeconds) { marchShot = true; Shot("2_march"); }
            if (arrivedAt >= 0 && !battleShot && world.Time - arrivedAt >= BattleShotSeconds) { battleShot = true; Shot("3_battle"); }
            if (battleShot && released >= Groups && bot.StrikePhaseNow == ZBot.StrikePhase.Waiting && world.Time - arrivedAt > 30) End("the pictures are taken");
        }

        static void RouteReset()
        {
            nextRouteLook = 0; worstOff = 0; offMembers = 0; joiners = 0; worstJoin = 0;
            offSince.Clear(); joinSince.Clear(); onRoute.Clear(); counted.Clear();
        }

        static double SegD(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
            double u = l2 <= 0 ? 0 : Mathf.Clamp01((float)(((px - ax) * vx + (py - ay) * vy) / l2));
            double qx = ax + u * vx - px, qy = ay + u * vy - py;
            return System.Math.Sqrt(qx * qx + qy * qy);
        }

        static void RouteLook()
        {
            var line = bot.StrikeRouteLine();
            if (line.Count < 2) return;
            var t = line[line.Count - 1];
            foreach (int id in bot.StrikeMemberIds())
            {
                var o = world.Get(id); if (o == null) continue;
                bool fights = o.AttackObject != null || (o.Waypoints.Count > 0 && (o.Waypoints[0].mode == WpMode.Attack || o.Waypoints[0].mode == WpMode.Agro));
                double dx = o.CenterX - t.x, dy = o.CenterY - t.y;
                if (fights || dx * dx + dy * dy <= (double)ZWorld.StrikeArrivePx * ZWorld.StrikeArrivePx) { offSince.Remove(id); continue; }
                double d = double.MaxValue;
                for (int i = 0; i + 1 < line.Count; i++) d = System.Math.Min(d, SegD(o.CenterX, o.CenterY, line[i].x, line[i].y, line[i + 1].x, line[i + 1].y));
                if (d <= 160)
                {
                    if (onRoute.Add(id) && joinSince.TryGetValue(id, out double js)) { joiners++; worstJoin = System.Math.Max(worstJoin, world.Time - js); }
                    offSince.Remove(id); continue;
                }
                if (!onRoute.Contains(id)) { if (!joinSince.ContainsKey(id)) joinSince[id] = world.Time; continue; }
                if (!offSince.TryGetValue(id, out double since)) offSince[id] = since = world.Time;
                worstOff = System.Math.Max(worstOff, world.Time - since);
                if (world.Time - since > 10.0 && counted.Add(id)) { offMembers++; Say($"OFF THE ROUTE: {o.Type} #{o.Id} at px ({o.CenterX},{o.CenterY}), {d:0} px from the group's route for more than 10 s"); }
            }
        }

        static void End(string why)
        {
            EditorApplication.update -= Step;
            var g = ZGame.I;
            if (g && sped) g.gameSpeed = speed0;
            if (camKept && rts) { rts.focus = focus0; rts.SetHeightImmediate(height0); }
            camKept = false; sped = false;
            int pics = (marchShot ? 1 : 0) + (battleShot ? 1 : 0) + (formingTaken ? 1 : 0);
            Report = $"{Tag} {(pics == 3 && released >= Groups ? "DONE" : "INCOMPLETE")}: {why}; groups formed {groupNo}, released {released} of {Groups} asked; pictures of the last group {pics}/3 (level {level}); text {Path.Combine(dir ?? "", "on_screen_" + level + ".txt").Replace('\\', '/')}";
            if (world != null && dir != null) { log.AppendLine(Report); File.WriteAllText(Path.Combine(dir, "on_screen_" + level + ".txt"), log.ToString()); }
            Debug.Log(Report);
            world = null; bot = null; stand = null;
        }
    }
}
