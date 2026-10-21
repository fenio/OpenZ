using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K331b checks (menu OpenZ/V1.1/K331b ...), items 331 + 332: every big falling piece and the rocks. Each check picks
    /// the rock nearest the view's middle, makes the death right beside it (on the side away from the camera's view of it), waits until
    /// and three pictures from the game camera's angle at the RTS distance (at least 45 m): the break, +1.5 s, at rest.
    /// <list type="bullet">
    /// <item>Tank: an enemy medium tank killed beside the rock (break-up at death forced): its turret's landing and its parts.</item>
    /// <item>Jeep: an enemy jeep killed beside the rock (the flip forced): the flipped hull.</item>
    /// <item>Squad: an enemy squad of five beside the rock thrown by a blast on its far side (their bodies fly at the rock; view only).</item>
    /// <item>Building: the building (not a fort) with a rock nearest its side destroyed: its heavy chunks.</item>
    /// <item>Shuttle (level 16): the playable ship flown over the nearest rocks and destroyed there in the air (a plain burst) or, with
    /// rifle fire from an enemy squad (a rifle-class kill: the crack in the air; with OzShuttleBreakup.DropWhole on, the whole-ship drop onto
    /// them).</item>
    /// <item>Tree (level 16): the tree with a rock nearest its foot felled toward the rock.</item>
    /// <item>Rock Under A Resting Body Shows Its Stump: a body resting on or against a rock; the rock shows its stump; the body must
    /// wake, fall and rest again, never in the rock.</item>
    /// </list>
    /// Wanted (PASS lines): big bodies lying in a rock at rest (a point 5 cm inside their own collider shape inside a rock's drawn shape) 0;
    /// body middles inside a rock 0; scripted landings (turret, tree) in a rock 0. Test tools only: nothing is saved.
    /// </summary>
    public static class OzK331bRockCheck
    {
        const string M = "OpenZ/V1.1/K331b ";
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k331b_rocks_check"); Directory.CreateDirectory(p); return p; } }
        static string name = "-", lead = "";
        static float until, shotAt = -1f, restAt = -1f, startAt;
        static int step, force0;
        static Vector3 at; static RockPillar rock;
        static float camDist = 55f, camPitch = 55f, camYaw;
        static readonly List<ZObject> made = new List<ZObject>();
        static int[] added0 = new int[OzRockBody.Kinds], touch0 = new int[OzRockBody.Kinds], onRock0 = new int[OzRockBody.Kinds], woken0 = new int[OzRockBody.Kinds], dropped0 = new int[OzRockBody.Kinds], started0 = new int[OzRockBody.Kinds];
        static int turretMoved0, turretKept0, bodyMoved0, bodyKept0, treeTurned0, treeKept0, dropTouches0, thudAsked0, thudPlayed0;
        static System.Func<bool> doneWhen; static System.Action after;

        // ------------------------------------------------------------------ helpers
        static bool Ready(out ZGame g)
        {
            g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null) { Debug.LogWarning("[OpenZ K331b] enter Play first (a level running), the camera on rocks"); return false; }
            return true;
        }

        static Vector3 ViewMiddle(ZGame g)
        {
            var cam = Camera.main;
            if (cam) { camPitch = cam.transform.eulerAngles.x; camYaw = cam.transform.eulerAngles.y; }
            var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            if (cam) camDist = Mathf.Clamp(Vector3.Distance(cam.transform.position, c), 45f, 80f);              // the RTS distance: high and wide
            return c;
        }

        static IEnumerable<RockPillar> Standing() => Object.FindObjectsByType<RockPillar>(FindObjectsSortMode.None).Where(r => r && r.pillar && r.pillar.activeInHierarchy);

        static RockPillar NearestRock(Vector3 c) => Standing().OrderBy(r => Flat(r.transform.position, c)).FirstOrDefault();

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static Bounds Box(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); var b = new Bounds(go.transform.position, Vector3.zero); bool any = false;
            foreach (var r in rs) { if (!r || !r.enabled) continue; if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; } }
            return b;
        }

        /// <summary>A free tile (vehicles: `robot` false) 2.5-8 m from the rock's side, on the side the camera looks from first.</summary>
        static bool SpotBeside(ZGame g, RockPillar r, bool robot, out int px, out int py)
        {
            px = py = 0; var W = g.World; var rb = Box(r.pillar);
            var cam = Camera.main; var toCam = cam ? cam.transform.position - rb.center : Vector3.back; toCam.y = 0f; toCam.Normalize();
            for (float d = 2.5f; d <= 8f; d += 0.5f)
                for (int s = 0; s < 16; s++)
                {
                    int turn = (s + 1) / 2 * ((s & 1) == 0 ? 1 : -1);
                    var dir = Quaternion.Euler(0f, turn * 22.5f, 0f) * toCam;
                    var p = rb.center + dir * (Mathf.Max(rb.extents.x, rb.extents.z) + d);
                    var q = ZGame.WorldToPx(p); int tx = q.x >> 4, ty = q.y >> 4;
                    bool ok = true;
                    for (int j = -1; j <= 1 && ok; j++) for (int i = -1; i <= 1 && ok; i++) ok = W.Path.TilePassable(tx + i, ty + j, robot);
                    if (!ok) continue;
                    if (W.Objects.Any(o => o != null && !o.Destroyed && o.Kind != ZKind.Robot && o.Contains(tx * 16 + 8, ty * 16 + 8))) continue;
                    px = tx * 16; py = ty * 16; return true;
                }
            return false;
        }

        static Team Enemy(ZGame g) { var me = g.World.Objects.FirstOrDefault(o => o != null && o.IsFort && o.Owner != Team.Neutral); return me != null && me.Owner == Team.Red ? Team.Blue : Team.Red; }

        static void Snapshot()
        {
            System.Array.Copy(OzRockBody.Added, added0, OzRockBody.Kinds); System.Array.Copy(OzRockBody.Touching, touch0, OzRockBody.Kinds);
            System.Array.Copy(OzRockBody.OnRockN, onRock0, OzRockBody.Kinds); System.Array.Copy(OzRockBody.WokenN, woken0, OzRockBody.Kinds);
            System.Array.Copy(OzRockBody.Dropped, dropped0, OzRockBody.Kinds); System.Array.Copy(OzRockBody.StartedIn, started0, OzRockBody.Kinds);
            turretMoved0 = OzRockSolid.TurretMoved; turretKept0 = OzRockSolid.TurretKept; bodyMoved0 = OzRockSolid.BodyMoved; bodyKept0 = OzRockSolid.BodyKept;
            treeTurned0 = OzRockSolid.TreeTurned; treeKept0 = OzRockSolid.TreeKept; dropTouches0 = OzRockSolid.DropTouches;
            thudAsked0 = OzBodySounds.ThudAsked; thudPlayed0 = OzBodySounds.ThudPlayed;
        }

        static void Shot(string tag)
        {
            try { var path = OpenZShot.Shot(at, camDist, camPitch, camYaw, Path.Combine(Out, "k331b_" + name + "_" + tag + ".png"), 1920, 1080); Debug.Log("[OpenZ K331b] picture " + path); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ K331b] picture " + tag + " failed: " + e.Message); }
        }

        static void Begin(string check, string what, float seconds, System.Func<bool> done, System.Action then = null)
        {
            name = check; lead = what; doneWhen = done; after = then; step = 0; shotAt = -1f; restAt = -1f; startAt = Time.time;
            until = Time.realtimeSinceStartup + seconds;
            Debug.Log("[OpenZ K331b] " + check + ": " + what);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        static bool AllRested(float radius)
        {
            foreach (var b in OzRockBody.All) if (b && Flat(b.transform.position, at) < radius && !b.Rested && b.gameObject.activeInHierarchy) return false;
            return Time.time - startAt > 3f;
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            if (step == 0) { Shot("1_break"); shotAt = Time.time + 1.5f; step = 1; }
            if (step == 1 && Time.time >= shotAt) { Shot("2_plus1_5s"); step = 2; }
            bool done = step >= 2 && (doneWhen == null || doneWhen());
            if (done && restAt < 0f) restAt = Time.time + 0.8f;
            if ((restAt >= 0f && Time.time >= restAt) || Time.realtimeSinceStartup > until)
            {
                EditorApplication.update -= Tick;
                Shot("3_rest");
                Report();
                after?.Invoke(); after = null;
            }
        }

        // ------------------------------------------------------------------ the checks
        [MenuItem(M + "Kill A Tank Beside The Nearest Rock (Play)")]
        static void Tank() => Vehicle("tank", "medium", 1);

        [MenuItem(M + "Kill A Jeep Beside The Nearest Rock: It Flips (Play)")]
        static void Jeep() => Vehicle("jeep", "jeep", 1);

        static void Vehicle(string check, string type, int force)
        {
            if (!Ready(out var g)) return;
            var c = ViewMiddle(g); rock = NearestRock(c);
            if (!rock) { Debug.LogWarning("[OpenZ K331b] no standing rock in this level"); return; }
            if (!SpotBeside(g, rock, false, out int px, out int py)) { Debug.LogWarning("[OpenZ K331b] no free spot beside " + rock.name); return; }
            var W = g.World; var o = W.CreateObject("vehicle", type, px, py, Enemy(g));
            if (o == null) { Debug.LogWarning("[OpenZ K331b] could not make a " + type); return; }
            g.SpawnUnitView(o); made.Add(o);
            at = ZGame.PxToWorld(o.CenterX, o.CenterY); at.y = g.GroundY(at.x, at.z);
            Snapshot(); force0 = WreckFx.Force; WreckFx.Force = force;
            EditorApplication.delayCall += () => KillLater(o, 1.0f);
            Begin(check, $"a {type} killed {Flat(at, rock.transform.position):0.0} m from {rock.name} (sim #{rock.simId}); outcomes forced (break-up / flip)", 34f,
                  () => Time.time - startAt > 9f && AllRested(40f), () => WreckFx.Force = force0);
        }

        static void KillLater(ZObject o, float delay)
        {
            float t0 = Time.realtimeSinceStartup;
            void Kill()
            {
                if (!Application.isPlaying) { EditorApplication.update -= Kill; return; }
                if (Time.realtimeSinceStartup - t0 < delay) return;
                EditorApplication.update -= Kill;
                var g = ZGame.I; if (!g || o == null || o.Destroyed) return;
                o.DamageHealth(o.Health); g.World.UpdateObjectHealth(o);
                startAt = Time.time; step = 0;
            }
            EditorApplication.update += Kill;
        }

        [MenuItem(M + "Blast A Squad Beside The Nearest Rock Into It (Play)")]
        static void Squad()
        {
            if (!Ready(out var g)) return;
            var c = ViewMiddle(g); rock = NearestRock(c);
            if (!rock) { Debug.LogWarning("[OpenZ K331b] no standing rock in this level"); return; }
            if (!SpotBeside(g, rock, true, out int px, out int py)) { Debug.LogWarning("[OpenZ K331b] no free spot beside " + rock.name); return; }
            var W = g.World; var sq = W.CreateRobotGroup("grunt", px, py, Enemy(g));
            if (sq == null) { Debug.LogWarning("[OpenZ K331b] could not make a squad"); return; }
            var all = new List<ZObject> { sq }; all.AddRange(sq.Minions);
            foreach (var o in all) g.SpawnUnitView(o);
            made.AddRange(all);
            at = ZGame.PxToWorld(sq.CenterX, sq.CenterY); at.y = g.GroundY(at.x, at.z);
            var rp = rock.transform.position; var blast = at + (at - rp).normalized * 2.5f;                       // on the far side: they fly at the rock
            Snapshot();
            EditorApplication.delayCall += () =>
            {
                float t0 = Time.realtimeSinceStartup;
                void Throw()
                {
                    if (!Application.isPlaying) { EditorApplication.update -= Throw; return; }
                    if (Time.realtimeSinceStartup - t0 < 1.0f) return;
                    EditorApplication.update -= Throw;
                    foreach (var o in all) if (g.Views.TryGetValue(o.Id, out var v) && v) v.Die(false, true, blast, -1, 1.5f);   // view only: the sim's robots are taken out below
                    startAt = Time.time; step = 0;
                }
                EditorApplication.update += Throw;
            };
            Begin("squad", $"{all.Count} robots {Flat(at, rp):0.0} m from {rock.name} (sim #{rock.simId}) thrown by a blast on their far side (view only)", 30f,
                  () => Time.time - startAt > 7f && AllRested(40f), () => { foreach (var o in all) if (o != null) o.KillAt = 0; });
        }

        [MenuItem(M + "Destroy The Building Nearest A Rock (Play)")]
        static void Building()
        {
            if (!Ready(out var g)) return;
            ViewMiddle(g);
            var rocks = Standing().ToList(); ZObject best = null; float bd = float.MaxValue; RockPillar br = null;
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Destroyed || o.Kind != ZKind.Building || o.IsFort || o.IsBridge) continue;
                if (!g.StaticViews.TryGetValue(o.Id, out var go) || !go || !go.GetComponent<BuildingBreaker>()) continue;
                var c = g.CenterWorld(o, true); float half = Mathf.Max(o.WidthPix, o.HeightPix) / 16f;
                foreach (var r in rocks) { float d = Flat(r.transform.position, c) - half; if (d < bd) { bd = d; best = o; br = r; } }
            }
            if (best == null) { Debug.LogWarning("[OpenZ K331b] no building (not a fort) in this level"); return; }
            rock = br; at = g.CenterWorld(best, true);
            Snapshot();
            best.DamageHealth(best.Health); g.World.UpdateObjectHealth(best);
            Begin("building", $"{best.Type} #{best.Id} destroyed, {rock.name} (sim #{rock.simId}) {bd:0.0} m from its side", 34f, () => Time.time - startAt > 10f && AllRested(45f));
        }

        [MenuItem(M + "Shuttle: Fly It Over The Nearest Rocks And Burst It There (Play, level 16)")]
        static void ShuttleAir() => Shuttle(false);

        [MenuItem(M + "Shuttle: Fly It Over The Nearest Rocks And Shoot It Down There By Rifle Fire (Play, level 16)")]
        static void ShuttleDrop() => Shuttle(true);

        static void Shuttle(bool drop)
        {
            if (!Ready(out var g)) return;
            var ps = OzPlayableShuttle.I; var s = ps ? ps.Obj : null;
            if (!ps || s == null || s.Destroyed || ps.BrokenUp) { Debug.LogWarning("[OpenZ K331b] no playable shuttle here (level 16)"); return; }
            ViewMiddle(g);
            rock = NearestRock(ps.transform.position);
            if (!rock) { Debug.LogWarning("[OpenZ K331b] no standing rock in this level"); return; }
            var rp = rock.transform.position; var q = ZGame.WorldToPx(rp);
            var me = s.Owner; var W = g.World;
            W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = q.x, y = q.y, playerGiven = true } });
            int hp0 = s.MaxHealth; s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);
            at = rp; Snapshot();
            float t0 = Time.realtimeSinceStartup; bool shot = false;
            void Fly()
            {
                if (!Application.isPlaying || s == null) { EditorApplication.update -= Fly; return; }
                bool there = Flat(ps.transform.position, rp) < 6f && ps.FeetHeight > 5f;
                if (!there && Time.realtimeSinceStartup - t0 < 70f) return;
                if (!shot)
                {
                    shot = true; s.MaxHealth = hp0; s.SetHealth(drop ? 1 : s.Health);
                    at = ps.transform.position; at.y = g.GroundY(at.x, at.z);
                    if (drop)
                    {   // an enemy rifle squad below: the first hit brings it down (a rifle-class kill: the crack, or the drop with its switch)
                        var sq = SpotBeside(g, rock, true, out int px, out int py) ? W.CreateRobotGroup("grunt", px, py, me == Team.Red ? Team.Blue : Team.Red) : null;
                        if (sq != null) { var list = new List<ZObject> { sq }; list.AddRange(sq.Minions); foreach (var o in list) g.SpawnUnitView(o); made.AddRange(list); W.SendWaypoints(sq.Owner, sq, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } }); }
                        else { s.DamageHealth(s.Health); W.UpdateObjectHealth(s); }
                    }
                    else { s.DamageHealth(s.Health); W.UpdateObjectHealth(s); }
                }
                if (!ps.BrokenUp && Time.realtimeSinceStartup - t0 < 120f) return;
                EditorApplication.update -= Fly;
                startAt = Time.time; step = 0;
                Begin(drop ? "shuttle_rifle" : "shuttle_air", $"the ship over {rock.name} (sim #{rock.simId}), {(drop ? "rifle fire (" + (OzShuttleBreakup.DropWhole ? "the whole-ship drop" : "the crack in the air") + ")" : "destroyed in the air: a burst")}; break: {OzShuttleBreakup.LastHow}", 34f,
                      () => !OzShuttleBreakup.Falling && OzShuttleBreakup.LastAllRested() && AllRested(50f),
                      () => { foreach (var o in made) if (o != null && o.Kind == ZKind.Robot) o.KillAt = 0; });
            }
            Debug.Log($"[OpenZ K331b] the shuttle flies to {rock.name} (sim #{rock.simId}); it is destroyed there");
            EditorApplication.update -= Fly; EditorApplication.update += Fly;
        }

        [MenuItem(M + "Fell The Tree Nearest A Rock Toward It (Play, level 16)")]
        static void Tree()
        {
            if (!Ready(out var g)) return;
            var tf = OzTreeFall.I; if (!tf) { Debug.LogWarning("[OpenZ K331b] no felled-tree manager here (level 16)"); return; }
            ViewMiddle(g);
            var fTrees = typeof(OzTreeFall).GetField("trees", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = fTrees != null ? fTrees.GetValue(tf) as System.Collections.IList : null;
            if (list == null) { Debug.LogWarning("[OpenZ K331b] the tree list cannot be read"); return; }
            var rocks = Standing().ToList(); object best = null; float bd = float.MaxValue; RockPillar br = null; ZObject bsim = null; Transform bh = null;
            foreach (var t in list)
            {
                var ty = t.GetType();
                var sim = ty.GetField("sim")?.GetValue(t) as ZObject; var holder = ty.GetField("holder")?.GetValue(t) as Transform;
                bool fallen = (bool)(ty.GetField("fallen")?.GetValue(t) ?? true);
                if (sim == null || sim.Destroyed || !holder || fallen) continue;
                foreach (var r in rocks) { float d = Flat(r.transform.position, holder.position); if (d < bd) { bd = d; best = t; br = r; bsim = sim; bh = holder; } }
            }
            if (best == null) { Debug.LogWarning("[OpenZ K331b] no standing tree"); return; }
            rock = br; at = bh.position;
            var away = rock.transform.position - bh.position; away.y = 0f; away.Normalize();
            var tyB = best.GetType();
            tyB.GetField("blast")?.SetValue(best, bh.position - away * 3f); tyB.GetField("blastAt")?.SetValue(best, Time.time);   // as a blast on its far side: it falls toward the rock
            Snapshot();
            bsim.DamageHealth(bsim.Health); g.World.UpdateObjectHealth(bsim);
            Begin("tree", $"the tree at {bh.position.x:0.0}, {bh.position.z:0.0} felled toward {rock.name} (sim #{rock.simId}) {bd:0.0} m away", 16f, () => Time.time - startAt > 9f);
        }

        [MenuItem(M + "Rock Under A Resting Body Shows Its Stump (Play, after a check)")]
        static void Swap()
        {
            if (!Ready(out var g)) return;
            OzRockBody on = null; RockPillar r0 = null; float bd = float.MaxValue;
            foreach (var b in OzRockBody.All)
            {
                if (!b || !b.Rested || b.TouchCount == 0) continue;
                foreach (var r in Standing())
                {
                    var rb = Box(r.pillar); rb.Expand(0.6f); var bb = Box(b.gameObject);
                    if (!rb.Intersects(bb)) continue;
                    float d = Vector3.Distance(rb.center, bb.center) - (b.OnRock ? 100f : 0f);
                    if (d < bd) { bd = d; on = b; r0 = r; }
                }
            }
            if (!on) { Debug.LogWarning("[OpenZ K331b] no body resting on or against a standing rock: run a check first"); return; }
            rock = r0; at = on.transform.position; ViewMiddle(g);
            Snapshot();
            Shot("0_before");
            rock.Damage();                                                                                     // view only: the stump (after its own 0-0.25 s delay)
            Begin("swap", $"{rock.name} (sim #{rock.simId}) under / beside a {OzRockBody.KindNames[(int)on.kind]} ({(on.OnRock ? "on it" : "against it")}) shows its stump", 20f,
                  () => Time.time - startAt > 4f && AllRested(30f));
        }

        [MenuItem(M + "State")]
        static void State() => Debug.Log("[OpenZ K331b] " + OzRockSolid.State + " | built ahead " + OzRockSolid.PrebuildState + " | " + OzRockBody.State + " | " + OzRockSolid.LandingState);

        // ------------------------------------------------------------------ the report
        static void Report()
        {
            var g = ZGame.I; var sb = new StringBuilder();
            sb.AppendLine("[OpenZ K331b] " + name + ": " + lead);
            OzRockSolid.ArmAround(at, 60f, 3f, 60f);                                                        // the rocks round it on for the measuring
            Physics.SyncTransforms();
            int big = 0, inRock = 0, mids = 0; string worst = "-";
            foreach (var b in OzRockBody.All)
            {
                if (!b || !b.gameObject.activeInHierarchy || Flat(b.transform.position, at) > 60f) continue;
                big++;
                if (b.InRock(out bool mid, out var which)) { inRock++; worst = $"{OzRockBody.KindNames[(int)b.kind]} {b.name} in {which}"; }
                if (mid) mids++;
                sb.AppendLine($"      {OzRockBody.KindNames[(int)b.kind],-15} {b.name,-28} {b.Size:0.0} m, {(b.Rested ? "rests" : "MOVING")}{(b.OnRock ? " on a rock" : b.TouchCount > 0 ? " against a rock" : "")}, held in a rock {b.HeldCount}");
            }
            int scripted = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {   // the tossed turrets lying near (scripted, no body)
                if (!t || t.parent || !t.GetComponentInChildren<Renderer>() || Flat(t.position, at) > 60f || t.GetComponent<OzRockBody>() || t.GetComponentInChildren<UnitView>() || !t.name.ToLowerInvariant().Contains("turret")) continue;
                var bb = Box(t.gameObject); bool inside = false;
                for (int i = 0; i < 9 && !inside; i++)
                {
                    var p = bb.center + Vector3.Scale(bb.extents * 0.8f, new Vector3(i % 3 - 1, 0f, i / 3 - 1)); p.y = bb.center.y;
                    inside = OzRockSolid.InsideRock(p, out _);
                }
                if (inside) { scripted++; worst = "turret " + t.name; }
            }
            int k(int[] a, int[] a0) { int s = 0; for (int i = 0; i < a.Length; i++) s += a[i] - a0[i]; return s; }
            sb.AppendLine(OzV11Kit.Verdict(big > 0 || name == "tree", $"big bodies near it made solid against the rocks: {big} (added +{k(OzRockBody.Added, added0)})"));
            sb.AppendLine(OzV11Kit.Verdict(inRock == 0, $"big bodies lying in a rock (a point 5 cm inside their own shape inside a rock's drawn shape): {inRock}{(inRock > 0 ? " (" + worst + ")" : "")}"));
            sb.AppendLine(OzV11Kit.Verdict(mids == 0, $"body middles inside a rock: {mids}"));
            sb.AppendLine(OzV11Kit.Verdict(scripted == 0, $"tossed turrets lying in a rock: {scripted}"));
            if (name == "swap") sb.AppendLine(OzV11Kit.Verdict(k(OzRockBody.WokenN, woken0) > 0, $"the body on / against the rock woke when it showed its stump: +{k(OzRockBody.WokenN, woken0)} (robot bodies dropped +{k(OzRockBody.Dropped, dropped0)})"));
            if (name == "swap" && k(OzRockBody.Dropped, dropped0) > 0) sb.AppendLine(OzV11Kit.Verdict(OzBodySounds.ThudAsked - thudAsked0 >= k(OzRockBody.Dropped, dropped0), $"a dropped robot body lands with its thud: asked +{OzBodySounds.ThudAsked - thudAsked0} (played +{OzBodySounds.ThudPlayed - thudPlayed0}) for {k(OzRockBody.Dropped, dropped0)} dropped"));
            if (name == "tree") sb.AppendLine(OzV11Kit.Verdict(OzRockSolid.TreeKept == treeKept0, $"the tree's fall: {OzRockSolid.LastTree} (turned +{OzRockSolid.TreeTurned - treeTurned0}, kept in a rock +{OzRockSolid.TreeKept - treeKept0})"));
            if (name == "tank") sb.AppendLine(OzV11Kit.Verdict(OzRockSolid.TurretKept == turretKept0, $"the turret's landing: {OzRockSolid.LastTurret} (moved +{OzRockSolid.TurretMoved - turretMoved0}, kept +{OzRockSolid.TurretKept - turretKept0})"));
            if (name == "shuttle_rifle") sb.AppendLine($"      the rifle kill: {OzShuttleBreakup.LastHow}, the dropped hull's points that met a rock +{OzRockSolid.DropTouches - dropTouches0}, breaks moved out of a rock {OzShuttleBreakup.RockShifts} (largest {OzShuttleBreakup.RockShiftLargest:0.00} m)");
            sb.AppendLine($"      rock contacts at rest +{k(OzRockBody.Touching, touch0)} (on a rock +{k(OzRockBody.OnRockN, onRock0)}), started in a rock +{k(OzRockBody.StartedIn, started0)}, woken +{k(OzRockBody.WokenN, woken0)}");
            sb.AppendLine("      " + OzRockSolid.State);
            sb.AppendLine("      built ahead " + OzRockSolid.PrebuildState + "; " + OzRockBody.State);
            sb.AppendLine("      " + OzRockSolid.LandingState);
            sb.AppendLine($"      pictures: {Out} (k331b_{name}_*.png)");
            var text = sb.ToString();
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(Out, "k331b_" + name + "_report.txt"), text); } catch (System.Exception) { }
        }
    }
}
