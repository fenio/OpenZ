using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace OpenZ
{
    /// <summary>
    /// K337's Play proof (menu OpenZ/V1.1/Shuttle: K337 Missed Air Shots Proof): while a fight at the flying shuttle runs (the K256b weapons
    /// gallery G, started by the same menu, or any fight), once a second: the drawn projectiles (live, the pool), the grenades, the kit's
    /// counts (OzAirShots), the ground effects under the flying ship (OzShuttleBreakup), the frame time and the memory (managed heap, Mono
    /// used, all allocated, garbage collections). Pictures of the first air bursts of misses (the frame it bursts and 0.3 s later, at the
    /// camera the gallery holds). At the end (the gallery done, at least MinSeconds; at most MaxSeconds) the summary with its PASS / FAIL
    /// lines. Output: the work tree's records, milestones/k337_missed_air_shots (found upward from the project at run time). Test only.
    /// </summary>
    public sealed class OzAirShotsLog : MonoBehaviour
    {
        public static float MinSeconds = 180f, MaxSeconds = 1200f;
        public static string Report = "idle";
        static OzAirShotsLog running;

        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "k337_missed_air_shots");
            return System.IO.Path.Combine(Application.persistentDataPath, "k337_missed_air_shots");
        }

        /// <summary>Starts the log (one at a time; a running one is ended and a new one begun).</summary>
        public static void Begin(float minSeconds)
        {
            if (running) Destroy(running.gameObject);
            MinSeconds = minSeconds;
            var go = new GameObject("OzAirShotsLog"); DontDestroyOnLoad(go);
            running = go.AddComponent<OzAirShotsLog>();
        }

        readonly StringBuilder log = new StringBuilder(), sum = new StringBuilder();
        string dir; float t0; int fails, passes, pictures;
        int maxLive, pool, rings0, gated0, popsSeen, lastPopFrame = -1;
        float frameSum; int frameN; float worstFrame;
        float maxArc; System.Action<int, FxWeapon, float, float, Vector3, bool> trace;   // the highest drawn point of a projectile at the flying ship (over the ground)
        // the live count by thirds of the take, the post-collection memory floor per 30 s
        readonly System.Collections.Generic.List<(float t, int live, long mono)> rows = new System.Collections.Generic.List<(float, int, long)>();

        void Start()
        {
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            t0 = Time.realtimeSinceStartup;
            OzAirShots.ResetCounts();
            trace = Trace; OpenZFxPlayer.ProjTrace += trace;                                              // made once: no garbage per projectile
            rings0 = OzShuttleBreakup.GroundFxUnderAirShip; gated0 = OzShuttleBreakup.AirBurstsGated;
            Report = "running";
            log.AppendLine("t s | frame ms (mean / worst) | projectiles live / pool | grenades | rounds at the ship, air bursts (at the cap / at the end / extras), not drawn again, hits | " +
                           "hard-cap ends, pool-full ends | ground fx under the flying ship | managed MB, Mono used MB, all allocated MB, collections");
            StartCoroutine(Run());
        }

        void Update()
        {
            frameSum += Time.unscaledDeltaTime; frameN++; worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
            var z = OpenZFxPlayer.I;
            if (z) { int n = z.LiveProjectiles; if (n > maxLive) maxLive = n; pool = Mathf.Max(pool, z.ProjectilePool); }
            if (OzAirShots.LastPopFrame != lastPopFrame && Time.frameCount - OzAirShots.LastPopFrame <= 1)   // run 27D: the pop is stamped in OpenZFxPlayer (order 110), after this Update
            {
                lastPopFrame = OzAirShots.LastPopFrame; popsSeen++;
                var cam = Camera.main;
                var vp = cam ? cam.WorldToViewportPoint(OzAirShots.LastPopAt) : Vector3.zero;
                bool seen = cam && vp.z > 0f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f;
                if (seen && pictures < 6) StartCoroutine(Picture(OzAirShots.LastPopEarly ? "early" : "end", OzAirShots.LastPopAt));
            }
        }

        IEnumerator Picture(string kind, Vector3 at)
        {
            int n = ++pictures;
            string a = $"K337_miss_air_burst_{n:00}_{kind}_h70.png", b = $"K337_miss_air_burst_{n:00}_{kind}_0.3s_h70.png";
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, a));
            log.AppendLine($"picture {a}: the air burst of a miss ({(kind == "early" ? "at its cap: a sure miss" : "at the rules' end point")}) at {at}, {at.y - (ZGame.I ? ZGame.I.GroundY(at.x, at.z) : 0f):0.0} m over the ground");
            float t = 0f; while (t < 0.3f) { t += Time.unscaledDeltaTime; yield return null; }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, b));
            log.AppendLine($"picture {b}");
        }

        bool GalleryRunning => OzShuttlePolishProof.Report != null && OzShuttlePolishProof.Report.StartsWith("running");

        IEnumerator Run()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                yield return wait;
                float t = Time.realtimeSinceStartup - t0;
                var z = OpenZFxPlayer.I; var gf = GrenadeFx.I;
                long managed = System.GC.GetTotalMemory(false), mono = Profiler.GetMonoUsedSizeLong(), all = Profiler.GetTotalAllocatedMemoryLong();
                int live = z ? z.LiveProjectiles : 0;
                rows.Add((t, live, mono));
                log.AppendLine($"{t,6:0} | {(frameN > 0 ? frameSum / frameN * 1000f : 0f),5:0.0} / {worstFrame * 1000f,5:0.0} | {live,3} / {(z ? z.ProjectilePool : 0),3} | {(gf ? gf.Active : 0),2} | " +
                               $"{OzAirShots.Recorded}, {OzAirShots.Pops} ({OzAirShots.PopsAtCap} / {OzAirShots.PopsAtEnd} / {OzAirShots.PopsFollowers}), {OzAirShots.AlreadyEnded}, {OzAirShots.HitsDrawn} | " +
                               $"{OzAirShots.CapEnds}, {OzAirShots.Evictions} | {OzShuttleBreakup.GroundFxUnderAirShip - rings0} | {managed / 1048576f:0.0}, {mono / 1048576f:0.0}, {all / 1048576f:0.0}, {System.GC.CollectionCount(0)}");
                frameSum = 0f; frameN = 0; worstFrame = 0f;
                if ((int)t % 10 == 0) Flush();
                if (t >= MaxSeconds || (t >= MinSeconds && !GalleryRunning)) break;
            }
            Summary();
        }

        void Check(string what, bool ok) { if (ok) passes++; else fails++; sum.AppendLine((ok ? "PASS " : "FAIL ") + what); }

        void Summary()
        {
            float gs = ZGame.I ? Mathf.Max(0.05f, ZGame.I.gameSpeed) : 1f;
            int n = rows.Count;
            sum.AppendLine($"K337 missed air shots, the Play log: {n} s; {OzAirShots.CountsText()}");
            sum.AppendLine($"ground light / rings under the flying ship during the log {OzShuttleBreakup.GroundFxUnderAirShip - rings0}, air bursts gated {OzShuttleBreakup.AirBurstsGated - gated0}");
            Check($"at most {OzAirShots.MaxLive} projectiles at once ({maxLive}, the pool {pool})", maxLive <= OzAirShots.MaxLive && pool <= OzAirShots.MaxLive);
            Check($"no drawn projectile older than its hard cap (the oldest {OzAirShots.MaxAgeEnded:0.00} s <= {OzAirShots.CapMaxSimS / gs + 0.05f:0.00} s)", OzAirShots.MaxAgeEnded <= OzAirShots.CapMaxSimS / gs + 0.05f);
            Check($"misses at the flying ship ended in small air bursts up there ({OzAirShots.Pops}: at the cap {OzAirShots.PopsAtCap}, at the end {OzAirShots.PopsAtEnd}, salvo extras {OzAirShots.PopsFollowers})", OzAirShots.Pops > 0);
            Check($"no ground light or ring under the flying ship ({OzShuttleBreakup.GroundFxUnderAirShip - rings0})", OzShuttleBreakup.GroundFxUnderAirShip - rings0 == 0);
            Check($"the arcs of the rounds at the flying ship stay under the ceiling (the highest {maxArc:0.0} m over the ground under it <= {OzAirShots.CeilingM + 4f:0} m: the ceiling over the lowest ground of its path + uneven ground; kept under it {OzAirShots.ArcsFlattened}; " +
                  $"MLRS shots at the flying ship without marked rockets {OzAirShots.MarksLeftOut})", maxArc <= OzAirShots.CeilingM + 4f);
            if (n >= 60)
            {
                int third = n / 3, a = 0, c = 0;
                for (int i = 0; i < third; i++) a = Mathf.Max(a, rows[i].live);
                for (int i = n - third; i < n; i++) c = Mathf.Max(c, rows[i].live);
                Check($"the live projectile count is flat (most at once: first third {a}, last third {c})", c <= a * 1.5f + 8f);
                // the Mono heap's floor (its lowest value per 30 s: after the collections) at the start and at the end of the take
                long f0 = long.MaxValue, f1 = long.MaxValue;
                for (int i = 0; i < n; i++) { if (rows[i].t >= 30f && rows[i].t < 60f) f0 = System.Math.Min(f0, rows[i].mono); if (i >= n - 30) f1 = System.Math.Min(f1, rows[i].mono); }
                if (f0 != long.MaxValue && f1 != long.MaxValue)
                {
                    float mins = Mathf.Max(0.5f, (rows[n - 1].t - 45f) / 60f), slope = (f1 - f0) / 1048576f / mins;
                    Check($"the memory does not climb: the Mono heap's floor {f0 / 1048576f:0.0} MB (30-60 s) -> {f1 / 1048576f:0.0} MB (the last 30 s): {slope:+0.00;-0.00} MB per minute (at most +2)", slope <= 2f);
                }
            }
            sum.AppendLine($"pictures: {pictures} (the first air bursts of misses on screen: K337_miss_air_burst_*_h70.png); {passes} PASS, {fails} FAIL");
            Report = fails == 0 ? "done: every check PASS" : "done: " + fails + " FAIL";
            Flush();
            Debug.Log("[OzAirShotsLog]\n" + sum);
            Destroy(gameObject);
        }

        void Flush()
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "k337_live_log.txt"), log.ToString());
                if (sum.Length > 0) System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "k337_summary.txt"), sum.ToString());
            }
            catch (System.Exception ex) { Debug.LogWarning("[OzAirShotsLog] " + ex.Message); }
        }

        /// <summary>OpenZFxPlayer.ProjTrace (every drawn projectile, every frame): the arcs of the rounds at the flying ship and their salvo extras.</summary>
        void Trace(int serial, FxWeapon w, float age, float life, Vector3 pos, bool eased)
        {
            if (!OzAirShots.IsBound(serial)) return;
            float h = pos.y - (ZGame.I ? ZGame.I.GroundY(pos.x, pos.z) : 0f);
            if (h > maxArc) maxArc = h;
        }

        void OnDestroy() { if (trace != null) OpenZFxPlayer.ProjTrace -= trace; if (running == this) running = null; }
    }
}
