using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K434 Play watch: the EASY computer takes its big decisions 30 % slower (rule DV127, Sim/ZBot.EasySlow.cs). Started from the menu
    /// OpenZ/V1.1/CPU: K434 Easy Slower Decisions Watch (Editor/OzEasySlowMenu.cs) in a level started on EASY, within its first 40 s of game
    /// time. It only reads the running game (no input, no order, no picture) and judges once the game clock passes 2:30 (later, up to 5:00,
    /// while no two rounds in a row fell in the same flag tier yet: W4 needs one such gap):
    ///  W1 the game runs EASY and its computer player is Easy with the rule at work;
    ///  W2 the first order round no sooner than 58.5 s and on the slower 2.6 s clock (was 46 s);
    ///  W3 production not slowed: its first production change from 45 s, before that first order round;
    ///  W4 every gap of two order rounds in the same flag tier as the rule says (13 / 18.2 / 26 / 39 s; was 10 / 14 / 20 / 30 s).
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/easy_cpu_slower_k434, found upward at run time). Test only.
    /// </summary>
    public sealed class OzEasySlowWatch : MonoBehaviour
    {
        public static string Report = "idle";
        public const double JudgeAt = 150.0, JudgeLatest = 300.0;
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "easy_cpu_slower_k434");
            return System.IO.Path.Combine(Application.persistentDataPath, "easy_cpu_slower_k434");
        }
        public static void Run()
        {
            Report = "running";
            new GameObject("OzEasySlowWatch").AddComponent<OzEasySlowWatch>();
        }

        ZGame g; ZWorld W; ZBot bot; double startedAt, lastRound = -1, lastD = -1, firstRound = -1, firstBuild = -1; int rounds; bool stable = true, done;
        readonly List<(double at, double gap, double d, bool stable)> gaps = new List<(double, double, double, bool)>();
        readonly StringBuilder sb = new StringBuilder(); int passes, fails;

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzEasySlowWatch] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        static string F(double v, string f = "0.00") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
        static double Expected(double d)
        {
            double tick = ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE * ZWorld.EasyDecisionSlow, delay = d * ZBot.EASY_ORDER_DELAY_SCALE * ZWorld.EasyDecisionSlow;
            return System.Math.Ceiling(delay / tick - 1e-6) * tick;
        }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null;
            if (W != null) foreach (var b in W.Bots) if (!b.Ignored) { bot = b; break; }
            startedAt = W != null ? W.Time : -1;
            Log($"K434 watch: started at game time {F(startedAt)} s; difficulty {(g ? OpenZDifficulty.Label(g.gameDifficulty) : "-")}; computer {(bot != null ? bot.Team + " " + bot.Difficulty + (bot.EasySlowActive ? " (K434 at work: x" + F(ZWorld.EasyDecisionSlow, "0.0") + ")" : " (K434 not at work)") : "none")}");
            if (bot != null && bot.LastRoundAt > 0) { lastRound = bot.LastRoundAt; firstRound = -2; Log("   (started after the first order round: W2 is not judged)"); }
        }

        void Update()
        {
            if (done || W == null || bot == null) { if (!done && (W == null || bot == null)) Finish(); return; }
            if (firstBuild < 0 && bot.BuildChanges > 0) { firstBuild = W.Time; Log($"first production change seen at {F(firstBuild)} s (read once a frame)"); }
            if (bot.LastRoundAt > 0 && bot.LastRoundAt != lastRound)
            {
                double t = bot.LastRoundAt; rounds++;
                if (firstRound == -1) { firstRound = t; Log($"first order round at {F(t)} s"); }
                else if (lastRound >= 0) { gaps.Add((t, t - lastRound, bot.LastDelay, stable && lastD == bot.LastDelay)); Log($"order round at {F(t)} s, {F(t - lastRound)} s after the last (flag tier delay {F(bot.LastDelay, "0")} s{(stable && lastD == bot.LastDelay ? "" : ", the tier changed meanwhile")})"); }
                lastRound = t; lastD = bot.LastDelay; stable = true;
            }
            else if (lastRound >= 0 && bot.LastDelay != lastD) stable = false;
            if (W.Time >= JudgeLatest || (W.Time >= JudgeAt && gaps.Exists(x => x.stable))) Finish();
        }

        void Finish()
        {
            done = true;
            Check($"W1 EASY with the rule at work: difficulty {(g ? OpenZDifficulty.Label(g.gameDifficulty) : "-")}, computer {(bot != null ? bot.Difficulty.ToString() : "none")}, at work {(bot != null && bot.EasySlowActive)}",
                  g && g.gameDifficulty == GameDifficulty.Easy && bot != null && bot.EasySlowActive);
            if (firstRound != -2)
            {
                double tick = ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE * ZWorld.EasyDecisionSlow;
                bool grid = firstRound > 0 && System.Math.Abs(firstRound / tick - System.Math.Round(firstRound / tick)) * tick < 0.02;
                Check($"W2 the first order round at {F(firstRound)} s (no sooner than {F(ZBot.EASY_START_DELAY * ZWorld.EasyDecisionSlow, "0.0")} s, on the {F(tick, "0.0")} s clock; was {F(46.0, "0")} s)",
                      firstRound >= ZBot.EASY_START_DELAY * ZWorld.EasyDecisionSlow - 1e-6 && grid);
                Check($"W3 production not slowed: first production change seen at {F(firstBuild)} s (from {F(ZBot.EASY_START_DELAY, "0")} s, before the first order round)",
                      firstBuild >= ZBot.EASY_START_DELAY - 1e-6 && (firstRound < 0 || firstBuild <= firstRound + 1e-6));
            }
            int same = 0, ok = 0; string bad = "";
            foreach (var x in gaps) if (x.stable) { same++; if (System.Math.Abs(x.gap - Expected(x.d)) < 0.02) ok++; else bad += $" {F(x.gap)} s (tier {F(x.d, "0")}, want {F(Expected(x.d), "0.0")})"; }
            Check($"W4 gaps of two rounds in the same flag tier as the rule says: {ok}/{same} (13 / 18.2 / 26 / 39 s by tier){(bad.Length > 0 ? "; NOT:" + bad : "")}", same > 0 && ok == same);
            Log($"RESULT {passes} PASS, {fails} FAIL; order rounds seen {rounds}, game time {F(W != null ? W.Time : -1, "0.0")} s");
            Report = $"{passes} PASS, {fails} FAIL";
            try
            {
                string dir = Folder(); System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "easy_slow_watch_log.txt"), sb.ToString());
            }
            catch (System.Exception ex) { Debug.LogWarning("[OzEasySlowWatch] log not written: " + ex.Message); }
            Destroy(gameObject);
        }
    }
}
