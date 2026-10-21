using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted Play checks of kit J15 (editor only). One at a time; each writes its report (ends "PASS n/n" when every line passed) and
    ///   J15 1: passive crocodiles sink     level 16: a robot walks to each passive crocodile's place and on across it; the rule is held
    ///                                      still meanwhile (no hunt starts; put back at the end). Wanted: every crossing entered the
    ///                                      animal's own room, the animal drawn there in 0 samples, it sank and came up again, the
    ///                                      quiet sounds in the sound log.
    ///   J15 2: both catches, every robot   level 16: each robot type (grunt, psycho, sniper, tough, pyro, laser) taken once by the slow
    ///                                      roll and once by the fast dive (forced, then put back to random). Wanted: taken; the catch
    ///                                      drawn; the robot's hips on the jaws' grip point in every held frame (at most 5 cm); the
    ///                                      jaws' grip point keeps its place on the jaw (spread under 0.25 m); the clack, the roll
    ///                                      splashes and the drag splash, or the dive splash, in the sound log (never the old low
    ///                                      drag-under sound); the robot gone under. One closer picture of each catch.
    ///   J15 3: flying bodies               any level: 8 robot bodies thrown at once where the camera looks, then 1 dropped. Wanted:
    ///                                      8 whooshes asked, 3 played (the cap), 5 dropped nearest-first; thuds played and never more
    ///                                      than 4 in a second; the dropped body: its own landing played a thud, no whoosh.
    /// Game speed 1 while a check runs (put back at its end).
    /// </summary>
    public static class OzCrocJ15Check
    {
        static string Folder { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "v1_1_20261012", "J15"); Directory.CreateDirectory(p); return p; } }
        static IEnumerator run; static string running = ""; static float speedWas = -1f;
        static ZGame G => ZGame.I; static ZWorld W => ZGame.I ? ZGame.I.World : null;
        public const float StandardHeight = 70f, CloseHeight = RtsCamera.ZoomClosest;   // the closest zoom (25 m; it was a fixed 22): hook of kit K18 (RtsCamera.ZoomLimits.cs)

        static void Begin(string name, IEnumerator e, bool l16)
        {
            if (!Application.isPlaying || !G || W == null) { Debug.LogWarning("[OpenZ J15] " + name + ": start Play on the level first"); return; }
            if (l16 && (G.level != "L16" || !CrocodileView.I)) { Debug.LogWarning("[OpenZ J15] " + name + ": level 16 with its crocodiles must be playing"); return; }
            if (run != null) { Debug.LogWarning("[OpenZ J15] " + running + " is still running: one check at a time"); return; }
            run = e; running = name; speedWas = G.gameSpeed; G.gameSpeed = 1f;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ J15] " + name + ": started at game speed 1");
        }

        static int lastFrame = -1;
        static void Tick()
        {
            if (!Application.isPlaying) { run = null; EditorApplication.update -= Tick; speedWas = -1f; Restore(); return; }
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            bool more;
            try { more = run != null && run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogError("[OpenZ J15] " + running + " threw " + ex); more = false; }
            if (!more) { run = null; EditorApplication.update -= Tick; if (speedWas > 0f && G) G.gameSpeed = speedWas; speedWas = -1f; Restore(); }
        }

        static bool ruleWas = true, ruleHeld;
        static void Restore()
        {
            CrocodileView.ForceCatch = 0;
            if (ruleHeld) { ZWorld.CrocodileDeviation = ruleWas; ruleHeld = false; }
        }

        sealed class Report
        {
            readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(); int pass, lines; readonly string name;
            public Report(string name) { this.name = name; sb.AppendLine(name + " on " + (G ? G.level : "?") + ", " + Screen.width + " x " + Screen.height + ", game speed 1"); }
            public void V(bool ok, string what) { lines++; if (ok) pass++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
            public void Note(string what) => sb.AppendLine(what);
            public void End()
            {
                sb.Append(pass == lines ? $"PASS {pass}/{lines}" : $"FAIL {pass}/{lines}");
                string path = Path.Combine(Folder, name.Split(':')[0].Replace(' ', '_') + "_" + (G ? G.level : "x") + ".txt");
                File.WriteAllText(path, sb.ToString());
                if (pass == lines) Debug.Log("[OpenZ J15] " + sb + "\n-> " + path); else Debug.LogError("[OpenZ J15] " + sb + "\n-> " + path);
            }
        }

        static IEnumerable Wait(double gameSeconds) { double end = W.Time + gameSeconds; while (W != null && W.Time < end) yield return null; }
        static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));
        static ZObject Obj(int id) => W.Get(id);
        static void Tough(int id) { var u = Obj(id); if (u == null) return; u.MaxHealth = u.Health = 100000000; foreach (var m in u.Minions) if (m != null) m.MaxHealth = m.Health = 100000000; }
        static void Remove(int id)
        {
            var u = Obj(id); if (u == null) return;
            var all = new List<ZObject> { u }; foreach (var m in u.Minions) if (m != null) all.Add(m);
            foreach (var m in all) { if (m.Destroyed) continue; m.MaxHealth = m.Health = 1; m.DamageHealth(2); W.UpdateObjectHealth(m, -1); }
        }
        static int Played(List<OzSoundLog.Entry> log, string key) => log.Count(e => e.key == key && e.outcome.StartsWith("PLAY"));
        static int Dropped(List<OzSoundLog.Entry> log, string key) => log.Count(e => e.key == key && e.outcome.StartsWith("DROP clip"));
        static bool Walkable(int tx, int ty) => tx >= 0 && ty >= 0 && tx < W.Map.width && ty < W.Map.height && W.Path.TilePassable(tx, ty, true);

        // ================================================================== 1 the passive sink
        [MenuItem("OpenZ/L16 Jungle/Checks/J15 1: passive crocodiles sink for passing robots (Play, level 16)")]
        static void SinkMenu() => Begin("J15 1 passive crocodiles sink", Sink(), true);

        static IEnumerator Sink()
        {
            var r = new Report("J15 1: passive crocodiles sink for passing robots");
            ruleWas = ZWorld.CrocodileDeviation; ruleHeld = true; ZWorld.CrocodileDeviation = false;     // the rule held still: no hunt during the crossings
            r.Note("the crocodile rule is held still during this check (no hunt starts) and put back at its end");
            OzCrocJ15Stats.Reset();
            float t0 = Time.unscaledTime; int tried = 0, crossed = 0, shots = 0;
            foreach (var c in W.Crocs.Where(x => !x.Busy).Take(8).ToList())
            {
                int ctx = (int)(c.X / 16), cty = (int)(c.Y / 16);
                // a start about 14 m away and the end on the far side, both walkable (wading allowed), 8 directions
                int sx = -1, sy = -1, ex = -1, ey = -1;
                for (int k = 0; k < 8 && sx < 0; k++)
                {
                    float a = k * Mathf.PI / 4f; int dx = Mathf.RoundToInt(Mathf.Cos(a) * 7f), dy = Mathf.RoundToInt(Mathf.Sin(a) * 7f);
                    if (Walkable(ctx - dx, cty - dy) && Walkable(ctx + dx, cty + dy)) { sx = ctx - dx; sy = cty - dy; ex = ctx + dx; ey = cty + dy; }
                }
                if (sx < 0) { r.Note($"croc {c.Index}: no walkable line across its place (skipped)"); continue; }
                tried++;
                var place = new Vector3((float)(c.X / 8.0), 0f, (float)(-c.Y / 8.0));
                OzL16Proof.Cam(place.x, place.z, StandardHeight);
                int id = OzL16Proof.Spawn("grunt", true, sx, sy); Tough(id);
                foreach (var _ in Wait(0.5)) yield return null;
                OzL16Proof.Move(id, ctx, cty);
                int cross0 = OzCrocJ15Stats.Crossings; bool shot = false;
                double end = W.Time + 30;
                while (W.Time < end)
                {
                    var o = Obj(id); if (o == null) break;
                    float d = Vector2.Distance(new Vector2(o.CenterX / 8f, -o.CenterY / 8f), new Vector2(place.x, place.z));
                    if (!shot && d < 1.5f && shots < 2) { shot = true; shots++; Shot($"J15_1_sink_croc{c.Index}_robot_on_its_place_h70"); }
                    if (d < 1.0f || o.Waypoints.Count == 0) break;
                    yield return null;
                }
                OzL16Proof.Move(id, ex, ey);
                foreach (var _ in Wait(12)) yield return null;
                if (OzCrocJ15Stats.Crossings > cross0) crossed++;
                Remove(id);
                foreach (var _ in Wait(4)) yield return null;                                    // it comes up again
            }
            var log = OzSoundLog.Since(t0).Where(e => e.src == "L16").ToList();
            r.V(tried >= 5, $"{tried} passive crocodiles had a walkable line across their place (wanted 5 or more)");
            r.V(crossed == tried, $"{crossed} of {tried} robots entered the animal's own room ({CrocodileView.SinkTouchM} m) on their way");
            r.V(OzCrocJ15Stats.SeenInside == 0, $"samples with a robot or vehicle inside a passive crocodile's room while it was drawn: {OzCrocJ15Stats.SeenInside} of {OzCrocJ15Stats.InsideSamples} (wanted 0)");
            r.V(OzCrocJ15Stats.Sinks >= crossed, $"sinks {OzCrocJ15Stats.Sinks} (wanted at least one per crossing)");
            r.V(OzCrocJ15Stats.Rises >= 1, $"it came up again afterwards: {OzCrocJ15Stats.Rises} rises");
            r.V(Played(log, "critter_croc_sink") >= 1 && Played(log, "critter_croc_resurface") >= 1, $"sound log: critter_croc_sink PLAY {Played(log, "critter_croc_sink")}, critter_croc_resurface PLAY {Played(log, "critter_croc_resurface")}");
            Restore();
            r.End();
        }

        // ================================================================== 2 both catches with each robot type
        static readonly string[] Types = { "grunt", "psycho", "sniper", "tough", "pyro", "laser" };

        [MenuItem("OpenZ/L16 Jungle/Checks/J15 2: both catches with each robot type (Play, level 16)")]
        static void CatchMenu() => Begin("J15 2 both catches", Catches(), true);

        static IEnumerator Catches()
        {
            var r = new Report("J15 2: both catches with each robot type");
            OzCrocJ15Stats.Reset();
            int pool = Enumerable.Range(0, W.CrocPoolCount).OrderByDescending(p => W.Crocs.Any(c => c.Pool == p) ? W.CrocPoolAttackM2(p) : -1).First();
            var cells = new List<Vector2Int>();
            for (int y = 2; y < W.Map.HeightPix; y += 4) for (int x = 2; x < W.Map.WidthPix; x += 4) if (W.CrocAttackWater(x, y) && W.CrocPoolAt(x, y) == pool) cells.Add(new Vector2Int(x, y));
            if (cells.Count == 0) { r.V(false, "no attack water on this level"); r.End(); yield break; }
            {   // the widest part of the attack water first (as Animals 2)
                var set = new HashSet<long>(); foreach (var c in cells) set.Add(((long)c.x << 20) | (uint)c.y);
                var dens = new Dictionary<Vector2Int, int>();
                foreach (var c in cells) { int k = 0; for (int dy = -40; dy <= 40; dy += 4) for (int dx = -40; dx <= 40; dx += 4) if (set.Contains(((long)(c.x + dx) << 20) | (uint)(c.y + dy))) k++; dens[c] = k; }
                cells.Sort((p, q) => dens[q].CompareTo(dens[p]));
            }
            var cell = cells[0];
            bool closeRoll = false, closeFast = false; int standardShots = 0;
            foreach (int kind in new[] { 1, 2 })
                foreach (var type in Types)
                {
                    string label = type + (kind == 2 ? " fast" : " roll");
                    CrocodileView.ForceCatch = kind;
                    bool close = kind == 1 ? !closeRoll : !closeFast;
                    OzL16Proof.Cam(cell.x / 8f, -cell.y / 8f, close ? CloseHeight : StandardHeight);
                    bool taken = false; float t0 = Time.unscaledTime; int helmets0 = CrocodileView.PartsFloated;
                    for (int attempt = 0; attempt < 6 && !taken; attempt++)
                    {
                        var robot = W.CreateRobotGroup(type, cell.x - 8, cell.y - 8, Team.Red, 1); G.SpawnUnitView(robot);
                        W.SendHold(Team.Red, robot);
                        int kills0 = W.CrocKills, hunts0 = W.CrocHunts;
                        foreach (var _ in Wait(ZWorld.CrocWetSeconds + 0.4)) yield return null;
                        bool started = W.CrocHunts > hunts0 || W.CrocStartHuntForTest(robot);
                        if (!started) { Remove(robot.Id); foreach (var _ in Wait(6)) yield return null; continue; }
                        bool shotBite = false, shotCatch = false;
                        double end = W.Time + 25;
                        while (W.Time < end && (W.CrocHuntsActive > 0 || W.CrocHunts == hunts0))
                        {
                            var c = W.Crocs.FirstOrDefault(x => x.Took && x.Busy);
                            if (c != null && c.State == ZCrocState.Lunge && !shotBite && !close && standardShots < 2) { shotBite = true; standardShots++; Shot($"J15_2_{type}_{(kind == 2 ? "fast" : "roll")}_bite_h70"); }
                            // the fast catch's close picture: inside the 1.0 s lunge, 0.15 s into the dive (0.87 s), the robot still in the mouth;
                            // a slow frame that steps past the lunge's end takes it in the first 0.2 s of the rule's next state (the dive goes on)
                            bool fastNow = c != null && ((c.State == ZCrocState.Lunge && W.Time - c.Since > ZWorld.CrocBiteSeconds + CrocodileView.FastDiveAfterBite + 0.15) || (c.State == ZCrocState.Thrash && W.Time - c.Since < 0.2));
                            if (c != null && close && !shotCatch && ((kind == 1 && c.State == ZCrocState.Thrash && W.Time - c.Since > 0.55) || (kind == 2 && fastNow)))
                            { shotCatch = true; Shot($"J15_2_{type}_{(kind == 2 ? "fast_dive" : "slow_roll")}_close_h22"); if (kind == 1) closeRoll = true; else closeFast = true; }
                            yield return null;
                        }
                        taken = W.CrocKills > kills0;
                        if (!taken) { Remove(robot.Id); foreach (var _ in Wait(6)) yield return null; continue; }
                        foreach (var _ in Wait(4)) yield return null;                               // under, the bubbles, the helmet
                        r.V(!(G.Views.TryGetValue(robot.Id, out var rv) && rv && rv.gameObject.activeSelf), $"{label}: the robot is gone under with the animal");
                    }
                    var log = OzSoundLog.Since(t0).Where(e => e.src == "L16").ToList();
                    OzCrocJ15Stats.CatchesBy.TryGetValue(label, out int seen);
                    OzCrocJ15Stats.GripBy.TryGetValue(label, out var g);
                    r.V(taken, $"{label}: taken");
                    r.V(seen >= 1, $"{label}: the {(kind == 2 ? "fast dive" : "slow roll")} was drawn ({seen})");
                    r.V(g != null && g.frames > 0 && g.max <= 0.05f, g == null ? $"{label}: no held frame sampled" : $"{label}: hips on the jaws' grip point in {g.frames} held frames: mean {g.sum / g.frames:0.000} m, max {g.max:0.000} m (wanted at most 0.05)");
                    if (g != null && g.jawMax >= 0f) r.V(g.jawMax - g.jawMin <= 0.25f, $"{label}: the jaws' grip point keeps its place on the jaw: {g.jawMin:0.00} to {g.jawMax:0.00} m from the jaw bone (spread wanted under 0.25 m)");
                    OzCrocJ15Stats.GripHeight.TryGetValue(type, out float gh);
                    r.Note($"{label}: hips {gh:0.00} m above the feet");
                    int clack = Played(log, "critter_croc_jaw_snap"), roll = Played(log, "critter_croc_roll_splash"), dive = Played(log, "critter_croc_dive_splash"), loop = log.Count(e => e.key == "critter_croc_thrash_loop" && e.outcome.StartsWith("LOOP World"));
                    int drag = Played(log, "critter_croc_drag_splash"), boom = log.Count(e => e.key == "critter_croc_drag_under");
                    r.V(clack >= 1 && Dropped(log, "critter_croc_jaw_snap") == 0, $"{label}: jaw clack PLAY {clack}");
                    if (kind == 1) r.V(roll >= 4 && dive == 0 && drag == 1 && boom == 0, $"{label}: roll splashes PLAY {roll} (wanted 4 or more), drag splash PLAY {drag} (wanted 1), dive splashes {dive} and old drag-under sounds {boom} (wanted 0)");
                    else r.V(dive == 1 && roll == 0 && loop == 0 && drag == 0 && boom == 0, $"{label}: dive splash PLAY {dive} (wanted 1), roll splashes {roll}, roll loops {loop}, drag splashes {drag} and old drag-under sounds {boom} (wanted 0)");
                    r.V(CrocodileView.PartsFloated > helmets0, $"{label}: the helmet came up");
                    foreach (var _ in Wait(3)) yield return null;
                }
            CrocodileView.ForceCatch = 0;
            r.Note($"catches drawn: roll {OzCrocJ15Stats.RollCatches}, fast {OzCrocJ15Stats.FastCatches}; clacks {OzCrocJ15Stats.Clacks}, roll splashes {OzCrocJ15Stats.RollSplashes}, drag splashes {OzCrocJ15Stats.DragSplashes}, dive splashes {OzCrocJ15Stats.DiveSplashes}");
            r.End();
        }

        // ================================================================== 3 flying bodies
        [MenuItem("OpenZ/Checks/J15 3: flying robot bodies whoosh and thud (Play, any level)")]
        static void BodiesMenu() => Begin("J15 3 flying bodies", Bodies(), false);

        static IEnumerator Bodies()
        {
            var r = new Report("J15 3: flying robot bodies whoosh and thud");
            var rts = Object.FindFirstObjectByType<RtsCamera>();
            if (!rts) { r.V(false, "no game camera"); r.End(); yield break; }
            rts.SetHeightImmediate(StandardHeight);
            var prefab = G.Prefab("Z_bs01_grunt") ?? G.Prefab("Z_robot");
            if (!prefab) { r.V(false, "no robot prefab"); r.End(); yield break; }
            yield return null;
            var focus = rts.focus; var made = new List<GameObject>();
            OzBodySounds.Reset(); float t0 = Time.unscaledTime;
            // eight bodies thrown in the same frame (a big blast)
            var bodies = new List<GameObject>();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f; var p = focus + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f; p.y = G.GroundY(p.x, p.z);
                var go = Object.Instantiate(prefab, p, Quaternion.Euler(0f, i * 45f, 0f)); go.name = "J15_check_body_" + i; made.Add(go); bodies.Add(go);
            }
            int launched = 0;                                                                    // thrown in the frame they are made (as CombatView does with a thrown driver)
            foreach (var go in bodies) if (RobotRagdoll.CanStart() && RobotRagdoll.Launch(go, go.transform.position - focus, 2.5f, 1f, (uint)(go.GetInstanceID() * 2654435761u))) launched++;
            yield return null; yield return null;
            Shot("J15_3_bodies_in_the_air_h70");
            foreach (var _ in Wait(4)) yield return null;
            int wAsk = OzBodySounds.WhooshAsked, wPlay = OzBodySounds.WhooshPlayed, wCap = OzBodySounds.WhooshCapped, tPlay = OzBodySounds.ThudPlayed, tAsk = OzBodySounds.ThudAsked;
            r.V(launched == 8, $"{launched} of 8 bodies thrown");
            r.V(wAsk == launched, $"whooshes asked {wAsk} (one per thrown body)");
            r.V(wPlay == Mathf.Min(launched, OzBodySounds.WhooshPerSecond) && wCap == launched - wPlay, $"whooshes played {wPlay}, dropped by the cap {wCap} (cap {OzBodySounds.WhooshPerSecond} per s)");
            r.V(tPlay >= 3, $"thuds played {tPlay} of {tAsk} landings (water {OzBodySounds.ThudOnWater}, off the picture {OzBodySounds.ThudOffScreen}, capped {OzBodySounds.ThudCapped})");
            var log = OzSoundLog.Since(t0).Where(e => e.src == "AD" && (e.key == OzBodySounds.ThudEvent || e.key == OzBodySounds.WhooshEvent)).ToList();
            var thudTimes = log.Where(e => e.key == OzBodySounds.ThudEvent && e.outcome.StartsWith("PLAY")).Select(e => e.t).ToList();
            int most = thudTimes.Count == 0 ? 0 : thudTimes.Max(t => thudTimes.Count(u => u >= t && u < t + 1f));
            r.V(most <= OzBodySounds.ThudPerSecond, $"at most {most} thuds in any second (cap {OzBodySounds.ThudPerSecond})");
            var clips = log.Where(e => e.outcome.StartsWith("PLAY")).Select(e => e.clip).Distinct().ToList();
            r.V(clips.Count >= 2, $"different takes heard: {string.Join(", ", clips)}");
            // one body that only drops (no throw): a thud, no whoosh
            var dp = focus + new Vector3(0f, 0f, 6f); dp.y = G.GroundY(dp.x, dp.z) + 1.5f;
            var drop = Object.Instantiate(prefab, dp, Quaternion.identity); drop.name = "J15_check_drop"; made.Add(drop);
            int w0 = OzBodySounds.WhooshAsked, tp0 = OzBodySounds.ThudPlayed, tw0 = OzBodySounds.ThudOnWater, to0 = OzBodySounds.ThudOffScreen;
            var drd = RobotRagdoll.CanStart() ? RobotRagdoll.Launch(drop, Vector3.forward, 0.3f, 0.2f, 12345u) : null;
            bool ok = drd;
            foreach (var _ in Wait(3)) yield return null;
            r.V(ok && OzBodySounds.WhooshAsked == w0, $"the dropped body: no whoosh ({OzBodySounds.WhooshAsked - w0})");
            var dl = drd ? drd.GetComponent<OzBodyLanding>() : null; string dThud = dl ? dl.thud : "no landing watch";
            // its own landing's outcome (late landings of the thrown bodies cannot count), and the counters moved with it
            r.V(dl && dl.landed && dThud == "PLAY" && OzBodySounds.ThudPlayed > tp0 && OzBodySounds.ThudOnWater == tw0 && OzBodySounds.ThudOffScreen == to0,
                $"the dropped body: landed {(dl && dl.landed ? "yes" : "no")}, its thud: {(dThud == "" ? "none asked" : dThud)}; since the drop: thuds played {OzBodySounds.ThudPlayed - tp0}, on water {OzBodySounds.ThudOnWater - tw0}, off the picture {OzBodySounds.ThudOffScreen - to0}");
            r.Note("sound log: " + string.Join(" > ", log.Select(e => e.key + " " + e.outcome + (e.clip != null ? " " + e.clip : ""))));
            foreach (var go in made) if (go) Object.Destroy(go);
            r.End();
        }
    }
}
