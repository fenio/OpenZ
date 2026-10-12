using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using Unity.Profiling;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// at a free lane (OwnerClipHelpers.FindLane): two passes, "before" (SquashedRemains.PerfLegacy: the old allocating squash + console
    /// lines) and "after" (pooled, quiet), each a red light tank driven head-on through a blue grunt squad and a red jeep through a second
    /// blue squad two of whose robots were just shot (fresh ragdolls under the wheels). Nobody shoots (damage / attack radius 0), the CPU
    /// bot is paused. Logs every hit's HP loss (head-on 25 %, glancing 10 %), the per-frame CPU main-thread time / GC alloc / physics time
    /// (frames.csv for the graph), the CPU cost of each hit and squash, and the run-over sounds from OzSoundLog (splat, bucket a / b, cries).
    /// </summary>
    [DefaultExecutionOrder(3100)]
    public sealed class RunOverPolishProof : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("task_runover_polish");
        public static string Status = "idle";
        static readonly StringBuilder log = new StringBuilder();
        readonly StringBuilder csv = new StringBuilder("pass,frame,t,dt_ms,main_ms,gc_kb,phys_ms,hits,squashes,ragdolls,make_ms,hit_ms\n");
        ProfilerRecorder rMain, rGc, rPhys;
        string pass; bool rec; int frame; float t0;
        int lastHits, lastSq, lastRd;
        readonly List<ZObject> staged = new List<ZObject>();
        readonly Dictionary<int, double> seenHit = new Dictionary<int, double>();
        readonly Dictionary<int, int> hpBefore = new Dictionary<int, int>();
        readonly Dictionary<string, List<float>> mainBy = new Dictionary<string, List<float>>();
        readonly Dictionary<string, List<float>> eventMain = new Dictionary<string, List<float>>();
        readonly Dictionary<string, float> gcEvents = new Dictionary<string, float>();
        int hpOk, hpBad;

        static void Log(string s)
        {
            log.AppendLine(System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + s);
            Debug.Log("[RunOverPolish] " + s); Status = s;
        }

        public static string Run(int tx, int ty)
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            log.Length = 0;
            var h = new GameObject("RunOverPolishProof").AddComponent<RunOverPolishProof>();
            h.StartCoroutine(h.Routine(tx, ty));
            return "run-over polish proof at " + tx + "," + ty;
        }

        static ProfilerRecorder Rec(ProfilerCategory c, string n) { try { return ProfilerRecorder.StartNew(c, n); } catch { return default; } }

        void OnDestroy() { rMain.Dispose(); rGc.Dispose(); rPhys.Dispose(); SquashedRemains.PerfLegacy = false; }

        static void Calm(ZObject o) { if (o == null) return; o.Damage = 0; o.AttackRadius = 0; foreach (var mn in o.Minions) { mn.Damage = 0; mn.AttackRadius = 0; } }
        static List<ZObject> Members(ZObject l) { var s = new List<ZObject> { l }; s.AddRange(l.Minions); return s; }
        static void Go(ZWorld W, ZObject v, int tx, int ty) =>
            W.SendWaypoints(v.Owner, v, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.ForceMove, x = tx * 16 + 8, y = ty * 16 + 8 } });

        IEnumerator Routine(int tx, int ty)
        {
            var g = ZGame.I; var W = g.World;
            Directory.CreateDirectory(Dir);
            rMain = Rec(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            rGc = Rec(ProfilerCategory.Memory, "GC Allocated In Frame");
            rPhys = Rec(ProfilerCategory.Physics, "Physics.Simulate");
            if (!rPhys.Valid) { rPhys.Dispose(); rPhys = Rec(ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"); }
            Log($"recorders: main {rMain.Valid}, gc {rGc.Valid}, physics {rPhys.Valid}; screen {Screen.width}x{Screen.height}; testmode {PlayerPrefs.GetInt("oz_testmode", 0)}; run-over deviation {ZWorld.RunOverDeviation}");
            var bots = new List<ZBot>(W.Bots); W.Bots.Clear();
            var rts = FindFirstObjectByType<RtsCamera>();
            if (rts) { var c = ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8); rts.focus = new Vector3(c.x, 0f, c.z); rts.SetHeightImmediate(42f); }
            float snd0 = Time.unscaledTime;
            yield return Pass("before", true, W, g, tx, ty - 5, ty - 2);
            yield return new WaitForSeconds(2f);
            yield return Pass("after", false, W, g, tx, ty + 2, ty + 5);
            SquashedRemains.PerfLegacy = false;

            // ---- (1) health
            Log($"(1) health: {hpOk} hits took the right share (head-on 25 % / glancing 10 % of max, min 1 HP), {hpBad} wrong; the HUD bars read ZObject.Health every frame (OpenZUnitOverlay / OpenZHud), so they drop on the hit frame");
            // ---- (2) frame times
            foreach (var p in new[] { "before", "after" })
            {
                mainBy.TryGetValue(p, out var all); eventMain.TryGetValue(p, out var ev); gcEvents.TryGetValue(p, out var gcE);
                if (all == null || all.Count == 0) continue;
                var srt = new List<float>(all); srt.Sort();
                float med = srt[srt.Count / 2], p99 = srt[Mathf.Min(srt.Count - 1, (int)(srt.Count * 0.99f))], mx = srt[srt.Count - 1];
                float evMax = 0f; if (ev != null) foreach (var x in ev) evMax = Mathf.Max(evMax, x);
                int over20 = 0; foreach (var x in all) if (x > 20f) over20++;
                Log($"(2) {p}: {all.Count} frames, CPU main thread median {med:F1} ms, p99 {p99:F1}, max {mx:F1}, frames over 20 ms {over20}; run-over event frames (+-1) max {evMax:F1} ms ({(ev != null ? ev.Count : 0)} frames), GC in event frames {gcE:F0} KB");
            }
            Log($"(2) CPU cost per call: squash Make max {SquashedRemains.MaxMakeMs:F2} ms (last {SquashedRemains.LastMakeMs:F2}), hit max {RunOverView.MaxHitMs:F2} ms (last {RunOverView.LastHitMs:F2})");
            Log($"(2) Make split: slowest {SquashedRemains.MaxSteps} | last {SquashedRemains.LastSteps} | {OpenZNukePrewarm.Status()}");
            // ---- (3) sounds
            var es = OzSoundLog.Since(snd0); var keys = new SortedDictionary<string, int>(); int cries = 0, crushCries = 0, oldThud = 0;
            foreach (var e in es)
            {
                if (e.key == null) continue;
                bool played = e.outcome != null && !e.outcome.StartsWith("DROP");
                if (e.key.StartsWith("death_cry_crush") && played) crushCries++;
                if (e.key.StartsWith("death_cry") && played) cries++;
                if ((e.key == "dst_ragdoll_impact" || e.key == "imp_bullet_metal") && played) oldThud++;
                if (e.key.StartsWith("runover") || e.key == "death_robot_crush" || e.key == "squash" || e.key.StartsWith("hit"))
                {
                    string k = e.src + ":" + e.key + (e.src == "AD" && played && !string.IsNullOrEmpty(e.clip) ? " [" + e.clip + "]" : "") + (played ? "" : " " + e.outcome);
                    int n; keys.TryGetValue(k, out n); keys[k] = n + 1;
                }
            }
            var ks = new List<string>(); foreach (var kv in keys) ks.Add(kv.Key + " x" + kv.Value);
            Log($"(3) sounds: {string.Join(", ", ks.ToArray())}; crush cries {crushCries} (want 0), all death cries {cries} (the 4 robots shot for the ragdolls may cry), old push thud / clang plays {oldThud} (want 0)");
            Log($"totals: hits {RunOverView.Hits}, glancing {RunOverView.Glances}, squashes {W.RunOverSquashes}, pancakes made {SquashedRemains.Made}, ragdolls launched {RobotRagdoll.Launched}");
            W.Bots.AddRange(bots);
            File.WriteAllText(Dir + "/frames.csv", csv.ToString());
            File.AppendAllText(Dir + "/proof_log.txt", log.ToString());
            Status = "DONE";
            Destroy(gameObject);
        }

        IEnumerator Pass(string name, bool legacy, ZWorld W, ZGame g, int tx, int rowA, int rowB)
        {
            SquashedRemains.PerfLegacy = legacy;
            var sqA = W.CreateRobotGroup("grunt", tx * 16, rowA * 16, Team.Blue);
            var sqB = W.CreateRobotGroup("grunt", tx * 16, rowB * 16, Team.Blue);
            foreach (var s in new[] { sqA, sqB }) { g.SpawnUnitView(s); foreach (var mn in s.Minions) g.SpawnUnitView(mn); Calm(s); staged.AddRange(Members(s)); }
            var tank = W.CreateObject("vehicle", "light", (tx - 10) * 16, rowA * 16, Team.Red); g.SpawnUnitView(tank); Calm(tank);
            var jeep = W.CreateObject("vehicle", "jeep", (tx - 10) * 16, rowB * 16, Team.Red); g.SpawnUnitView(jeep); Calm(jeep);
            foreach (var o in staged) hpBefore[o.Id] = o.Health;
            yield return new WaitForSeconds(1.5f);
            var tv = g.Views.TryGetValue(tank.Id, out var tview) ? tview : null; var jv = g.Views.TryGetValue(jeep.Id, out var jview) ? jview : null;
            int tc = tv ? tv.GetComponentsInChildren<Collider>(true).Length : -1, jc = jv ? jv.GetComponentsInChildren<Collider>(true).Length : -1;
            Log($"pass {name} (PerfLegacy {legacy}): blue squads {Members(sqA).Count} + {Members(sqB).Count}, red light tank {tank.Id} (view colliders {tc}), red jeep {jeep.Id} (view colliders {jc}); ragdoll layer {BuildingBreaker.DebrisLayer}");
            pass = name; rec = true; frame = 0; t0 = Time.unscaledTime;
            lastHits = RunOverView.Hits; lastSq = SquashedRemains.Made; lastRd = RobotRagdoll.Launched;
            Go(W, tank, tx + 10, rowA);
            yield return new WaitForSeconds(3f);
            int shot = 0;
            foreach (var m in new List<ZObject>(sqB.Minions)) { if (shot >= 2) break; if (m.Destroyed) continue; m.DamageHealth(m.Health); W.UpdateObjectHealth(m, jeep.Id); shot++; }
            Log($"pass {name}: shot {shot} of the second squad (bullet deaths) so the jeep drives over fresh ragdolls");
            yield return new WaitForSeconds(0.35f);
            Go(W, jeep, tx + 10, rowB);
            yield return new WaitForSeconds(6.5f);
            rec = false;
            int sq = 0; foreach (var o in Members(sqA)) if (o.Squashed) sq++; foreach (var o in Members(sqB)) if (o.Squashed) sq++;
            Log($"pass {name} done: {frame} frames, squashed {sq}, pancakes this pass {SquashedRemains.Made - lastSq}, ragdolls this pass {RobotRagdoll.Launched - lastRd}");
        }

        void Update()
        {
            var g = ZGame.I; if (!g || g.World == null) return;
            // (1) every fresh hit on a staged robot: HP lost vs the rule
            foreach (var o in staged)
            {
                if (o.RunOverAt < 0 || (seenHit.TryGetValue(o.Id, out var at) && at == o.RunOverAt)) continue;
                seenHit[o.Id] = o.RunOverAt;
                int before = hpBefore.TryGetValue(o.Id, out var hb) ? hb : o.MaxHealth;
                int want = Mathf.Max(1, (int)(o.MaxHealth * (o.RunOverGlancing ? ZWorld.RunOverGlanceFrac : ZWorld.RunOverHurtFrac)));
                int expect = Mathf.Max(1, before - want);
                int now = o.Squashed ? -1 : o.Health;
                bool ok = o.Squashed || now == expect;
                if (ok) hpOk++; else hpBad++;
                Log($"(1) {pass} hit robot {o.Id} {(o.RunOverGlancing ? "glancing" : "head-on")}: HP {before} -> {(o.Squashed ? "squashed" : now.ToString())} of {o.MaxHealth} (want -{want} = {expect}, {(float)want / o.MaxHealth:P0}) {(ok ? "OK" : "WRONG")}");
                hpBefore[o.Id] = o.Squashed ? 0 : o.Health;
            }
            if (!rec) return;
            frame++;
            float main = rMain.Valid ? rMain.LastValue / 1e6f : Time.unscaledDeltaTime * 1000f;
            float gc = rGc.Valid ? rGc.LastValue / 1024f : 0f, ph = rPhys.Valid ? rPhys.LastValue / 1e6f : -1f;
            int dh = RunOverView.Hits - lastHits, ds = SquashedRemains.Made - lastSq, dr = RobotRagdoll.Launched - lastRd;
            csv.Append(pass).Append(',').Append(frame).Append(',').Append((Time.unscaledTime - t0).ToString("F3")).Append(',')
               .Append((Time.unscaledDeltaTime * 1000f).ToString("F2")).Append(',').Append(main.ToString("F2")).Append(',').Append(gc.ToString("F1")).Append(',')
               .Append(ph.ToString("F2")).Append(',').Append(dh).Append(',').Append(ds).Append(',').Append(dr).Append(',')
               .Append(ds > 0 ? SquashedRemains.LastMakeMs.ToString("F2") : "").Append(',').Append(dh > 0 ? RunOverView.LastHitMs.ToString("F2") : "").Append('\n');
            if (!mainBy.TryGetValue(pass, out var l)) mainBy[pass] = l = new List<float>();
            l.Add(main);
            // the recorders report the PREVIOUS frame, so an event's cost shows in this frame or the next: keep both
            if (dh + ds + dr > 0 || pendingEvent)
            {
                if (!eventMain.TryGetValue(pass, out var e)) eventMain[pass] = e = new List<float>();
                e.Add(main); gcEvents.TryGetValue(pass, out var gsum); gcEvents[pass] = gsum + gc;
            }
            pendingEvent = dh + ds + dr > 0;
            lastHits = RunOverView.Hits; lastSq = SquashedRemains.Made; lastRd = RobotRagdoll.Launched;
        }
        bool pendingEvent;
    }
}
