using System;
using System.Collections.Generic;
using System.Text;

namespace OpenZ
{
    // ============================================================================================================================
    //
    // Simulate() on Unity's bundled .NET). AudioDirector asks Decide() for every robot death, plays the chosen clip as a 3D,
    // distance-attenuated one-shot and reports what really sounded with Commit(); hit grunts use PickGrunt() / CommitGrunt().
    //  - death type -> pools: Bullet, Blast (explosions: flying screams), Fire (panicked), Laser (zap + yelp), Crush (squish + squeak;
    //    Zod has no run-over kill, so: a falling turret / wreck / rock chunk, or AudioDirector.MarkCrushed from a future run-over).
    //  - voiceless share per type (a robot body breaking: metal crack, crunch, servo dying, sparks + clattering parts, pops, short
    //    circuits, fizzing power-down): bullet 40 %, blast 60 %, fire 30 %, laser 40 %, crush 70 %. On top of that a death whose cry
    //    would break the limiter goes voiceless, so busy fights get MORE voiceless deaths, never a wall of screams.
    //  - cry limiter across the whole battlefield: at most 2 cries in any 1 s window and >= 0.35 s between two cries (= 1-2 / s).
    //    Voiceless: at most 4 in any 1 s (a grenade killing a squad = 1 cry + bodies); beyond that the death stays silent (the blast /
    //    ragdoll already sound).
    //  - random with no repeat of the last 8 clips (separate histories for cries, bodies and grunts; pools share clips, so the
    //    history is by clip name). Pools smaller than 9 keep at least one clip free.
    //    two long screams or two glitch lines back to back sounded alike; bodies: the name without its _a / _b variant letter:
    //    short_circuit_a then _b). Sniped drivers are always voiceless (Rachel / Alan already speak on every snipe).
    // ============================================================================================================================
    public sealed class DeathVoices
    {
        public enum Cause { Bullet = 0, Blast = 1, Fire = 2, Laser = 3, Crush = 4 }
        public enum Kind { Drop = 0, Voice = 1, Body = 2 }
        public struct Plan { public Kind kind; public Cause cause; public int clip; public int bodyClip; }

        public static readonly string[] CauseNames = { "bullet", "blast", "fire", "laser", "crush" };
        /// <summary>Table event of the death cries of a type (bus Voice/Robots, 3D).</summary>
        public static string CryEvent(Cause c) => "death_cry_" + CauseNames[(int)c];
        /// <summary>Table event of the voiceless body deaths of a type (bus SFX/Impacts, 3D). death_robot / death_robot_melt keep their ids.</summary>
        public static string BodyEvent(Cause c)
        {
            switch (c)
            {
                case Cause.Blast: return "death_robot_blast";
                case Cause.Fire: return "death_robot_melt";
                case Cause.Laser: return "death_robot_zap";
                case Cause.Crush: return "death_robot_crush";
                default: return "death_robot";
            }
        }
        public const string GruntEvent = "hit_grunt";

        /// <summary>One picker for the whole game (outlives a level restart, like RadioDiscipline.Shared).</summary>
        public static readonly DeathVoices Shared = new DeathVoices();

        // ---- tuning (AudioDirector copies its inspector values in every frame it uses it)
        public readonly float[] VoicelessShare = { 0.40f, 0.60f, 0.30f, 0.40f, 0.70f };
        public float CryWindow = 1f, CryGap = 0.35f, BodyWindow = 1f, BodyGap = 0f;
        public int CriesPerWindow = 2, BodiesPerWindow = 4, NoRepeat = 8;
        public float GruntGap = 0.9f, GruntAfterCry = 0.4f;

        readonly List<double> cries = new List<double>(), bodies = new List<double>();
        readonly List<string> cryHist = new List<string>(), bodyHist = new List<string>(), gruntHist = new List<string>();
        double lastCry = -1e9, lastBody = -1e9, lastGrunt = -1e9;

        // ---- counters (check menu, coverage notes)
        public int Cries, Bodies, Grunts, Deaths, Dropped;
        public readonly int[] DeathsBy = new int[5], CriesBy = new int[5];

        public void Reset()
        {
            cries.Clear(); bodies.Clear(); cryHist.Clear(); bodyHist.Clear(); gruntHist.Clear();
            lastCry = lastBody = lastGrunt = -1e9; Cries = Bodies = Grunts = Deaths = Dropped = 0;
            Array.Clear(DeathsBy, 0, 5); Array.Clear(CriesBy, 0, 5);
        }

        static void Prune(List<double> l, double now, double win) { l.RemoveAll(t => now - t >= win); }
        public bool CryFree(double now) { Prune(cries, now, CryWindow); return now - lastCry >= CryGap && cries.Count < CriesPerWindow; }
        public bool BodyFree(double now) { Prune(bodies, now, BodyWindow); return now - lastBody >= BodyGap && bodies.Count < BodiesPerWindow; }
        public float Share(Cause c) => VoicelessShare[(int)c];

        /// <summary>Sound family of a clip: death cries by style (alan_death_long_noooo -> alan_death_long), other clips without their
        /// variant letter (sd_robot_death_short_circuit_b -> sd_robot_death_short_circuit).</summary>
        public static string Family(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return "";
            if (clip.StartsWith("alan_death_"))
            {
                int k = clip.IndexOf('_', "alan_death_".Length);
                return k > 0 ? clip.Substring(0, k) : clip;
            }
            int u = clip.LastIndexOf('_');
            return u > 0 && clip.Length - u == 2 && char.IsLetter(clip[u + 1]) ? clip.Substring(0, u) : clip;
        }

        /// <summary>Random index into clips, never one of the last NoRepeat names of hist (at least one clip stays free), and not of
        /// the last clip's family while another family is free.</summary>
        public int PickIndex(IList<string> clips, List<string> hist, Func<double> rnd)
        {
            int n = clips?.Count ?? 0;
            if (n == 0) return -1;
            if (n == 1) return 0;
            int keep = Math.Min(NoRepeat, n - 1);
            var recent = new HashSet<string>();
            for (int i = hist.Count - 1; i >= 0 && recent.Count < keep; i--) recent.Add(hist[i]);
            var free = new List<int>(n);
            for (int i = 0; i < n; i++) if (!recent.Contains(clips[i])) free.Add(i);
            if (free.Count == 0) for (int i = 0; i < n; i++) free.Add(i);
            if (hist.Count > 0 && free.Count > 1)
            {
                string last = Family(hist[hist.Count - 1]);
                var other = free.FindAll(i => Family(clips[i]) != last);
                if (other.Count > 0) free = other;
            }
            int k = (int)(rnd() * free.Count); if (k >= free.Count) k = free.Count - 1;
            return free[k];
        }

        /// <summary>What a robot death of this type should sound like now. clip = index into cryClips (Voice) or bodyClips (Body);
        /// bodyClip = the voiceless fallback when the cry cannot sound (culled / no free voice), -1 = none.</summary>
        public Plan Decide(Cause c, double now, IList<string> cryClips, IList<string> bodyClips, Func<double> rnd, bool voiceless = false)
        {
            var p = new Plan { cause = c, kind = Kind.Drop, clip = -1, bodyClip = -1 };
            bool hasCry = cryClips != null && cryClips.Count > 0, hasBody = bodyClips != null && bodyClips.Count > 0;
            bool wantVoice = rnd() >= Share(c) && !voiceless;
            if (voiceless && hasBody) hasCry = false;                                                   // sniped driver: a body death only
            if (hasBody && BodyFree(now)) p.bodyClip = PickIndex(bodyClips, bodyHist, rnd);
            if (hasCry && (wantVoice || !hasBody) && CryFree(now)) { p.kind = Kind.Voice; p.clip = PickIndex(cryClips, cryHist, rnd); }
            else if (p.bodyClip >= 0) { p.kind = Kind.Body; p.clip = p.bodyClip; }
            return p;
        }

        /// <summary>A death happened (counted even when nothing could sound).</summary>
        public void Noted(Cause c) { Deaths++; DeathsBy[(int)c]++; }

        /// <summary>What really played (Kind.Drop = nothing).</summary>
        public void Commit(Kind k, Cause c, string clip, double now)
        {
            if (k == Kind.Voice) { cries.Add(now); lastCry = now; Push(cryHist, clip); Cries++; CriesBy[(int)c]++; }
            else if (k == Kind.Body) { bodies.Add(now); lastBody = now; Push(bodyHist, clip); Bodies++; }
            else Dropped++;
        }

        /// <summary>Hit grunt: index into clips, or -1 while the grunt limiter is shut (>= GruntGap between grunts across the map,
        /// none within GruntAfterCry of a death cry).</summary>
        public int PickGrunt(IList<string> clips, double now, Func<double> rnd)
        {
            if (clips == null || clips.Count == 0) return -1;
            if (now - lastGrunt < GruntGap || now - lastCry < GruntAfterCry) return -1;
            return PickIndex(clips, gruntHist, rnd);
        }
        public void CommitGrunt(string clip, double now) { lastGrunt = now; Push(gruntHist, clip); Grunts++; }

        void Push(List<string> h, string clip) { h.Add(clip ?? ""); if (h.Count > 32) h.RemoveAt(0); }

        public string Summary() =>
            $"deaths {Deaths} (bullet {DeathsBy[0]}, blast {DeathsBy[1]}, fire {DeathsBy[2]}, laser {DeathsBy[3]}, crush {DeathsBy[4]}): " +
            $"{Cries} cries, {Bodies} voiceless, {Dropped} silent (limiter / culled); {Grunts} hit grunts";

        // ============================================================================================================================
        // Offline self-check (tools/harness + the editor check menu): a battlefield of random deaths, checked against the rules.
        // ============================================================================================================================
        public sealed class SimResult { public bool pass; public string report; }

        /// <summary>pools: event id -> clip names (from the patched sound table). deathsPerSecond: mean rate (Poisson), with bursts
        /// (a grenade / shell kills 2-4 in the same frame) in 15 % of the events.</summary>
        public static SimResult Simulate(IDictionary<string, List<string>> pools, int seed, double seconds, double deathsPerSecond, double[] causeMix = null, double burstChance = 0.15)
        {
            causeMix = causeMix ?? new[] { 0.55, 0.22, 0.09, 0.10, 0.04 };
            var rng = new Random(seed);
            Func<double> rnd = () => rng.NextDouble();
            var dv = new DeathVoices();
            var sb = new StringBuilder();
            var cryTimes = new List<double>(); var cryNames = new List<string>(); var bodyNames = new List<string>();
            int[] want = new int[5], voiced = new int[5];
            int fails = 0;
            double t = 0, last = 0;
            List<string> P(string id) => pools.TryGetValue(id, out var l) ? l : new List<string>();
            while (t < seconds)
            {
                t += -Math.Log(1 - rng.NextDouble()) / deathsPerSecond;
                int burst = rng.NextDouble() < burstChance ? 2 + rng.Next(3) : 1;
                double u = rng.NextDouble(), acc = 0; int ci = 0;
                for (int i = 0; i < 5; i++) { acc += causeMix[i]; if (u <= acc) { ci = i; break; } }
                for (int b = 0; b < burst; b++)
                {
                    var c = (Cause)ci; double now = Math.Max(last, t); last = now;          // a burst = one frame (same time), like the game
                    var cry = P(CryEvent(c)); var body = P(BodyEvent(c));
                    dv.Noted(c); want[ci]++;
                    var plan = dv.Decide(c, now, cry, body, rnd);
                    string name = plan.kind == Kind.Voice ? cry[plan.clip] : plan.kind == Kind.Body ? body[plan.clip] : null;
                    dv.Commit(plan.kind, c, name, now);
                    if (plan.kind == Kind.Voice) { cryTimes.Add(now); cryNames.Add(name); voiced[ci]++; }
                    else if (plan.kind == Kind.Body) bodyNames.Add(name);
                }
            }
            // rule 1: <= 2 cries in any 1 s window, >= 0.35 s apart
            int maxWin = 0; double minGap = 1e9;
            for (int i = 0; i < cryTimes.Count; i++)
            {
                int n = 0; for (int j = i; j < cryTimes.Count && cryTimes[j] - cryTimes[i] < 1.0; j++) n++;
                maxWin = Math.Max(maxWin, n);
                if (i > 0) minGap = Math.Min(minGap, cryTimes[i] - cryTimes[i - 1]);
            }
            if (maxWin > 2 || minGap < 0.35 - 1e-9) fails++;
            // rule 2: no clip repeats within the last 8 of its kind (pool size allowing)
            int rep = Repeats(cryNames, 8) + Repeats(bodyNames, 8);
            if (rep > 0) fails++;
            int fam = 0; for (int i = 1; i < cryNames.Count; i++) if (Family(cryNames[i]) == Family(cryNames[i - 1])) fam++;
            int famB = 0; for (int i = 1; i < bodyNames.Count; i++) if (Family(bodyNames[i]) == Family(bodyNames[i - 1])) famB++;
            if (fam > 0.03 * Math.Max(1, cryNames.Count) || famB > 0.03 * Math.Max(1, bodyNames.Count)) fails++;
            double share = dv.Deaths > 0 ? 1.0 - (double)dv.Cries / dv.Deaths : 0;
            sb.AppendLine($"seed {seed}, {seconds:0} s at {deathsPerSecond:0.##} deaths/s{(burstChance > 0 ? "" : ", single deaths only")}: {dv.Summary()}");
            sb.AppendLine($"  voiceless or silent {share:P0} of deaths; max cries in 1 s {maxWin}, min gap {(cryTimes.Count > 1 ? minGap.ToString("0.00") : "-")} s; repeats inside 8: {rep}; same family back to back: cries {fam}, bodies {famB}");
            for (int i = 0; i < 5; i++) if (want[i] > 0) sb.AppendLine($"  {CauseNames[i],-6} {want[i],5} deaths, {voiced[i] * 100.0 / want[i],5:0.0} % cried (target {100 - dv.VoicelessShare[i] * 100:0} % when the limiter is open)");
            return new SimResult { pass = fails == 0, report = sb.ToString() };
        }

        static int Repeats(List<string> names, int window)
        {
            int n = 0;
            for (int i = 0; i < names.Count; i++)
                for (int j = Math.Max(0, i - window); j < i; j++)
                    if (names[j] == names[i]) { n++; break; }
            return n;
        }
    }
}
