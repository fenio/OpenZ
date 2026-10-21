#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K438 Play watch (editor only): a robot of the player's team under fire runs to no flag by itself (rule DV130, Sim/ZObject.NoAutoFlag.cs).
    /// Started from the menu OpenZ/V1.1/Robots: K438 No Flag Run Under Fire Watch (Editor/OzNoAutoFlagMenu.cs) in a running level. It only reads
    /// the running game (no input, no order, no picture). Every frame it looks at each squad leader of the player's team: a waypoint onto a
    /// flag that is not his, not given by the player, is a flag run of its own. Judged after 3:00 of game time (or when the level ends):
    ///  W1 the rule is on;
    ///  W2 no flag run of its own started while the squad was already under fire (under fire in the frame before and in the frame it began);
    ///  W3 no flag run of its own walked under fire longer than one half-second look (0.6 s of game time): its waypoint in front (an attack,
    ///     a chase, a dodge or a step aside in front of it is the squad fighting or moving aside, not running for the flag);
    ///  W4 the rule was at work or nothing asked for it: the count of runs held / ended / flags beside the way left alone is logged.
    /// Every flag run (of its own or clicked), every hold and every end is logged with the game time and the unit.
    /// Output: <see cref="Folder"/> (the records folder found upward from the project at run time). Test only.
    /// </summary>
    public sealed class OzNoAutoFlagWatch : MonoBehaviour
    {
        public static string Report = "idle";
        public const double JudgeAfter = 180.0, OneLook = 0.6;
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "no_auto_flag_k438");
            return System.IO.Path.Combine(Application.persistentDataPath, "no_auto_flag_k438");
        }
        public static void Run()
        {
            Report = "running";
            new GameObject("OzNoAutoFlagWatch").AddComponent<OzNoAutoFlagWatch>();
        }

        sealed class Seen { public bool own, clicked, fire; public double fireSince = -1; public int flag = -1; }

        ZGame g; ZWorld W; double startedAt; bool done;
        int held0, dropped0, way0, starts, startsUnderFire, keptLong, clicks; double longest;
        readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
        readonly StringBuilder sb = new StringBuilder(); int passes, fails;

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzNoAutoFlagWatch] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        static string F(double v, string f = "0.00") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
        static string Clock(double s) => $"{(int)(s / 60)}:{(int)(s % 60):00}";

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null;
            startedAt = W != null ? W.Time : -1;
            held0 = ZWorld.NoAutoFlagHeld; dropped0 = ZWorld.NoAutoFlagDropped; way0 = ZWorld.NoAutoFlagWayHeld;
            Log($"K438 watch: started at game time {Clock(startedAt)}; the player's team {(g ? g.player.ToString() : "-")}; rule {(ZWorld.NoAutoFlagUnderFireDeviation ? "ON" : "off")}; calm span {F(ZWorld.NoAutoFlagCalmSec, "0.0")} s");
        }

        static int FlagOf(ZWorld w, ZObject o, bool player)
        {
            foreach (var q in o.Waypoints)
            {
                if (q.mode != WpMode.Move || q.playerGiven != player || q.refId < 0) continue;
                var f = w.Get(q.refId);
                if (f != null && f.Kind == ZKind.MapItem && f.Type == "flag" && f.Owner != o.Owner) return f.Id;
            }
            return -1;
        }

        void Update()
        {
            if (done) return;
            if (W == null || g == null || g.World != W) { Finish(); return; }
            double now = W.Time;
            int h = ZWorld.NoAutoFlagHeld - held0, d = ZWorld.NoAutoFlagDropped - dropped0, wy = ZWorld.NoAutoFlagWayHeld - way0;
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Robot || o.IsMinion || o.Destroyed || o.KillAt >= 0 || o.Owner != g.player) continue;
                if (!seen.TryGetValue(o.Id, out var s)) seen[o.Id] = s = new Seen();
                bool fire = o.SquadUnderFire;
                int own = FlagOf(W, o, false), click = FlagOf(W, o, true);
                bool ownFront = own >= 0 && o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Move && !o.Waypoints[0].playerGiven && o.Waypoints[0].refId == own;   // the run itself is walked (not an attack / chase / dodge in front of it)
                if (own >= 0 && !s.own)
                {
                    starts++;
                    bool bad = fire && s.fire;
                    if (bad) startsUnderFire++;
                    Log($"{Clock(now)} {o.Type} #{o.Id}: flag run of its own to flag #{own}{(bad ? "  <- STARTED UNDER FIRE" : fire ? " (fire began this frame)" : "")}");
                }
                if (own < 0 && s.own) Log($"{Clock(now)} {o.Type} #{o.Id}: its own flag run ended{(fire ? " (under fire)" : "")}");
                if (click >= 0 && !s.clicked) { clicks++; Log($"{Clock(now)} {o.Type} #{o.Id}: the player's flag run to flag #{click}{(fire ? " (under fire: obeyed)" : "")}"); }
                if (ownFront && fire)
                {
                    if (s.fireSince < 0) s.fireSince = now;
                    double k = now - s.fireSince;
                    if (k > longest) longest = k;
                    if (k > OneLook && s.flag != -2) { keptLong++; s.flag = -2; Log($"{Clock(now)} {o.Type} #{o.Id}: its own flag run WALKED under fire for more than {F(OneLook, "0.0")} s"); }
                }
                else s.fireSince = -1;
                if (own < 0) s.flag = -1;
                s.own = own >= 0; s.clicked = click >= 0; s.fire = fire;
            }
            if (h + d + wy > 0 && sb.Length < 200000 && (h != lastH || d != lastD || wy != lastW))
            {
                if (d != lastD) Log($"{Clock(now)} the rule ended {d - lastD} flag run(s) of its own under fire (total {d})");
                lastH = h; lastD = d; lastW = wy;
            }
            if (now - startedAt >= JudgeAfter || W.GameOver) Finish();
        }
        int lastH, lastD, lastW;

        void Finish()
        {
            if (done) return;
            done = true;
            int h = ZWorld.NoAutoFlagHeld - held0, d = ZWorld.NoAutoFlagDropped - dropped0, wy = ZWorld.NoAutoFlagWayHeld - way0;
            Check($"W1 the rule is on (ZWorld.NoAutoFlagUnderFireDeviation)", ZWorld.NoAutoFlagUnderFireDeviation);
            Check($"W2 flag runs of its own started while the squad was under fire: {startsUnderFire} (of {starts} own runs seen)", startsUnderFire == 0);
            Check($"W3 flag runs of its own walked under fire longer than one look ({F(OneLook, "0.0")} s): {keptLong} (longest {F(longest)} s)", keptLong == 0);
            Check($"W4 the rule's own counts: idle looks that held a flag run {h}, own runs ended under fire {d}, flags beside the way left alone {wy}; the player's flag runs {clicks}", true);
            Log($"RESULT {passes} PASS, {fails} FAIL; game time {Clock(W != null ? W.Time : -1)}");
            Report = $"{passes} PASS, {fails} FAIL";
            try
            {
                string dir = Folder(); System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "no_auto_flag_watch_log.txt"), sb.ToString());
            }
            catch (System.Exception ex) { Debug.LogWarning("[OzNoAutoFlagWatch] log not written: " + ex.Message); }
            Destroy(gameObject);
        }
    }
}
#endif
